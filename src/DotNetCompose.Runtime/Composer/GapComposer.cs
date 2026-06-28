using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using DotNetCompose.Runtime.CompositionLocal;
using DotNetCompose.Runtime.Effects;
using DotNetCompose.Runtime.Snapshots;

namespace DotNetCompose.Runtime.Composer
{
    public class GapComposer : AbstractComposer
    {
        private readonly SlotTable _slotTable = new SlotTable();
        private ChangeList? _changeList;
        private ComposerChangeListWriter? _changeListWriter;
        private SlotReader? _reader;

        private int _nodeIndex;
        private int _slotIndex;

        // --- Invalidation system ---
        private RecomposeScopeOwner? _owner;
        private readonly List<Invalidation> _invalidations = new();
        internal readonly Stack<RecomposeScopeImpl> InvalidateStack = new();

        internal CompositionContext CompositionContext { get; } = new CompositionContext();
        internal RememberManager RememberManager { get; } = new RememberManager();
        internal SlotTable SlotTable => _slotTable;
        internal SlotReader Reader => _reader!;
        internal ChangeList? OperationsChangeList => _changeList;

        internal RecomposeScopeOwner? Owner
        {
            get => _owner;
            set => _owner = value;
        }

        internal void Drain()
        {
            if (_changeList != null && !_changeList.IsEmpty())
            {
                _slotTable.Write<object?>(writer =>
                {
                    _changeList.Drain(NoOpApplier.Instance, writer, RememberManager);
                    return null;
                });
            }
        }

        public override void ComposeContent(ComposableAction content)
        {
            CompositionContext.ClearUnused();
            _changeList = new ChangeList();
            _reader = _slotTable.CreateReadSnapshot();
            _changeListWriter = new ComposerChangeListWriter(this, _changeList, _reader);
            _nodeIndex = 0;
            _slotIndex = 0;

            _reader.StartGroup();
            _changeListWriter?.EnsureRootStarted();
            base.StartRoot();
            try
            {
                content(this, default, default);
            }
            finally
            {
                _reader.EndGroup();
                _changeListWriter.MoveReaderToAbsolute(_reader.CurrentGroup);
                _changeListWriter.FinalizeComposition();
                base.EndRoot();
            }
        }

        internal void Recompose(ComposableAction? content = null)
        {
            if (_changeList == null)
            {
                _changeList = new ChangeList();
                _reader = _slotTable.CreateReadSnapshot();
                _changeListWriter = new ComposerChangeListWriter(this, _changeList, _reader);
            }

            _nodeIndex = 0;
            _slotIndex = 0;

            _reader.StartGroup();
            _changeListWriter?.EnsureRootStarted();
            base.StartRoot();

            try
            {
                if (content != null)
                {
                    content(this, default, default);
                }
                else
                {
                    SkipCurrentGroup();
                }
            }
            finally
            {
                _reader.EndGroup();
                _changeListWriter.MoveReaderToAbsolute(_reader.CurrentGroup);
                _changeListWriter.FinalizeComposition();
                base.EndRoot();
            }

            _invalidations.Clear();
        }

        public override void StartGroup(int key)
        {
            base.StartGroup(key);
            _reader.StartGroup();
            _changeListWriter?.StartGroup(key);
        }

        public override void StartMovableGroup(int key)
        {
            base.StartGroup(key);
            _reader.StartGroup();
            _changeListWriter?.StartGroup(key);
            _changeListWriter?.SetGroupFlags(GroupFlags.IsMovableContent);
        }

        public override void EndMovableGroup(int key)
        {
            _changeListWriter?.EndCurrentGroup();
            _reader.EndGroup();
            _changeListWriter?.MoveReaderToAbsolute(_reader.CurrentGroup);
            base.EndGroup();
        }

        // --- Restartable groups ---

        public override void StartRestartableGroup(int key)
        {
            base.StartRestartableGroup(key);

            if (_reader.Parent >= _reader.Size)
            {
                var scope = new RecomposeScopeImpl(_owner, key);
                InvalidateStack.Push(scope);
                _changeListWriter?.UpdateValue(scope, _slotIndex++);
            }
            else
            {
                var invalidation = RemoveLocation(_reader.Parent);
                var slot = _reader.Next();
                var scope = slot as RecomposeScopeImpl;
                if (scope == null)
                {
                    scope = new RecomposeScopeImpl(_owner, key);
                    _changeListWriter?.UpdateValue(scope, _slotIndex++);
                }
                else
                {
                    _slotIndex++;
                }
                scope.RequiresRecompose = invalidation != null || scope.ForcedRecompose;
                if (scope.ForcedRecompose) scope.ForcedRecompose = false;
                InvalidateStack.Push(scope);
            }
        }

        public override IComposeUpdateScope? EndRestartableGroup(int key)
        {
            RecomposeScopeImpl? scope = InvalidateStack.Count > 0 ? InvalidateStack.Pop() : null;

            if (scope != null)
            {
                scope.RequiresRecompose = false;
                if (scope.Anchor == null)
                {
                    var anchor = _reader.Anchor(_reader.Parent);
                    scope.SetAnchor(anchor);
                }
                scope.DefaultsInvalid = false;
            }

            _changeListWriter?.EndCurrentGroup();
            _reader.EndGroup();
            _changeListWriter?.MoveReaderToAbsolute(_reader.CurrentGroup);
            base.EndGroup();

            if (scope != null && !scope.Skipped && (scope.Used || false))
            {
                return scope;
            }
            return null;
        }

        // --- Invalidation management ---

        internal void UpdateComposerInvalidations(Dictionary<RecomposeScopeImpl, object> invalidationsRequested)
        {
            for (int i = _invalidations.Count - 1; i >= 0; i--)
            {
                var inv = _invalidations[i];
                var anchor = inv.Scope.Anchor;
                if (anchor != null && anchor.Valid)
                {
                    if (inv.Location != anchor.Location)
                        inv.Location = anchor.Location;
                }
                else
                {
                    _invalidations.RemoveAt(i);
                }
            }

            foreach (var kvp in invalidationsRequested)
            {
                var scope = kvp.Key;
                var anchor = scope.Anchor;
                if (anchor == null || !anchor.Valid) continue;
                _invalidations.Add(new Invalidation(scope, anchor.Location, kvp.Value));
            }

            _invalidations.Sort(InvalidationLocationAscending.Instance);
        }

        internal Invalidation? FirstInvalidationInRange(int start, int end)
        {
            for (int i = 0; i < _invalidations.Count; i++)
            {
                var inv = _invalidations[i];
                if (inv.Location >= start && inv.Location < end)
                    return inv;
                if (inv.Location >= end)
                    break;
            }
            return null;
        }

        internal Invalidation? RemoveLocation(int location)
        {
            for (int i = 0; i < _invalidations.Count; i++)
            {
                if (_invalidations[i].Location == location)
                {
                    var result = _invalidations[i];
                    _invalidations.RemoveAt(i);
                    return result;
                }
            }
            return null;
        }

        internal void RemoveRange(int start, int end)
        {
            _invalidations.RemoveAll(inv => inv.Location >= start && inv.Location < end);
        }

        // --- Group skipping / recomposition ---

        public void SkipCurrentGroup()
        {
            if (_invalidations.Count == 0)
            {
                SkipGroup();
            }
            else
            {
                _reader.StartGroup();
                RecomposeToGroupEnd();
                _reader.EndGroup();
            }
        }

        private void SkipGroup()
        {
            _reader.SkipGroup();
        }

        private void RecomposeToGroupEnd()
        {
            var current = _reader.CurrentGroup;
            var end = _reader.CurrentEnd;

            while (!_reader.IsGroupEnd)
            {
                var inval = FirstInvalidationInRange(current, end);
                if (inval != null)
                {
                    inval.Scope.Compose(this);
                }
                else
                {
                    SkipGroup();
                }
            }
        }

        public override void SkipToGroupEnd()
        {
            SkipCurrentGroup();
        }

        // --- Node management ---

        public override void CreateNode<T>(Func<T> factory)
        {
            var node = factory();
            base.StartGroup(_nodeIndex);
            _reader.StartGroup();
            var isNew = _reader.Parent >= _reader.Size;
            if (isNew)
            {
                _changeListWriter?.StartNode(_nodeIndex, node);
            }
            else
            {
                _changeListWriter?.StartNodeReuse(_nodeIndex, node);
                _changeListWriter?.MoveDown(node);
                _changeListWriter?.UseNode(node);
                _changeListWriter?.UpdateNode<object, object>(node, (_, _) => { });
            }
            _nodeIndex++;
        }

        public override void EndGroup()
        {
            var isNode = _reader.IsNode;
            if (isNode)
                _changeListWriter?.MoveUp();
            _changeListWriter?.EndCurrentGroup();
            _reader.EndGroup();
            _changeListWriter?.MoveReaderToAbsolute(_reader.CurrentGroup);
            base.EndGroup();
        }

        public override bool Changed<T>(T value)
        {
            if (GroupDepth == 0) return true;
            int idx = _slotIndex++;
            if (_reader.Parent >= _reader.Size)
            {
                _changeListWriter?.UpdateValue(value, idx);
                return true;
            }
            var old = _reader.GroupGet(_reader.Parent, idx);
            if (!Equals(old, value))
            {
                _changeListWriter?.UpdateValue(value, idx);
                return true;
            }
            return false;
        }

        public override object? RememberedValue()
        {
            if (GroupDepth == 0) return ComposerStatics.Empty;
            int idx = _slotIndex++;
            if (_reader.Parent >= _reader.Size) return ComposerStatics.Empty;
            var val = _reader.GroupGet(_reader.Parent, idx);
            if (val is RememberObserverHolder holder)
                val = holder.Observer;
            return val ?? ComposerStatics.Empty;
        }

        public override void UpdateRememberedValue(object? value)
        {
            if (value == ComposerStatics.Empty) return;
            int idx = _slotIndex - 1;
            if (idx < 0) return;

            object toStore = value;
            if (value is IRememberObserver observer)
            {
                toStore = new RememberObserverHolder(observer);
                _changeListWriter?.Remember((RememberObserverHolder)toStore);
            }

            _changeListWriter?.UpdateValue(toStore, idx);
        }

        public override void ApplyNode<T>(Action<T> block, object? value)
        {
            if (value is T node)
                block(node);
        }

        // --- MovableContent support ---

        public void InsertMovableContent<TParam>(MovableContent<TParam> content, TParam param)
        {
            int key = RuntimeHelpers.GetHashCode(content);

            StartMovableGroup(key);

            var stateRef = CompositionContext.TakeMovableContentState(
                content as MovableContent<object?>, param!);

            if (stateRef != null && _changeListWriter != null)
            {
                ScheduleCopyMovableContent(stateRef);
            }

            content.Content(param);

            EndMovableGroup(key);
        }

        internal void ScheduleCopyMovableContent(MovableContentStateReference stateRef)
        {
            if (_changeListWriter != null)
            {
                _changeListWriter.CopySlotTableToAnchorLocation(
                    null, CompositionContext, stateRef, stateRef);
            }
        }

        internal void ExtractCurrentMovableContent(MovableContent<object?> content, object? param)
        {
            if (_changeListWriter == null) return;

            var anchor = _reader?.Anchor(_reader.CurrentGroup);
            if (anchor == null) return;

            var stateRef = new MovableContentStateReference(
                content, param, new SlotTable(), anchor, CurrentLocalMap);

            _changeListWriter.ReleaseMovableGroupAtCurrent(
                null!, CompositionContext, stateRef);
        }

        // --- CompositionLocal support ---

        public override void StartProviders(ProvidedValue[] values)
        {
            base.StartProviders(values);
            if (_changeListWriter != null && values != null && values.Length > 0)
            {
                _changeListWriter.UpdateAuxData(CurrentLocalMap);
            }
        }

        public override void EndProviders()
        {
            base.EndProviders();
        }

        public override T Consume<T>(CompositionLocal<T> key)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));

            var map = CurrentLocalMap;
            if (map != CompositionLocalMap.Empty)
            {
                var holder = map.GetHolder(key);
                if (holder != null)
                    return (T?)holder.ReadValue(map) ?? default!;
            }

            if (_reader != null)
            {
                int group = _reader.Parent;
                while (group > 0)
                {
                    if (_reader.HasAuxAt(group))
                    {
                        var aux = _reader.GroupGetAux(group);
                        if (aux is CompositionLocalMap groupMap)
                        {
                            var holder = groupMap.GetHolder(key);
                            if (holder != null)
                                return (T?)holder.ReadValue(groupMap) ?? default!;
                        }
                    }
                    group = _reader.GetParentGroup(group);
                }
            }

            var defaultVal = key.DefaultValueHolder.ReadValue(CompositionLocalMap.Empty);
            return defaultVal is T t ? t : default!;
        }
    }
}
