using System;
using System.Collections.Generic;
using DotNetCompose.Runtime.Snapshots;

namespace DotNetCompose.Runtime.Composer
{
    internal class RecomposeScopeImpl : IComposeUpdateScope
    {
        [Flags]
        private enum ScopeFlags
        {
            Used = 1,
            DefaultsInScope = 2,
            DefaultsInvalid = 4,
            RequiresRecompose = 8,
            Skipped = 16,
            Rereading = 32,
            ForcedRecompose = 64,
        }

        private ScopeFlags _flags;
        private int _currentToken;
        private RecomposeScopeOwner? _owner;
        private Action<IComposerContext>? _block;
        private Dictionary<IStateObject, List<WeakReference<RecomposeScopeImpl>>>? _trackedInstances;
        private GapAnchor? _anchor;
        private int _groupKey;

        public GapAnchor? Anchor => _anchor;
        public int GroupKey => _groupKey;

        internal void SetAnchor(GapAnchor anchor) => _anchor = anchor;

        public RecomposeScopeImpl(RecomposeScopeOwner? owner, int groupKey, GapAnchor? anchor = null)
        {
            _owner = owner;
            _groupKey = groupKey;
            _anchor = anchor;
        }

        public bool Used
        {
            get => _flags.HasFlag(ScopeFlags.Used);
            set => SetFlag(ScopeFlags.Used, value);
        }

        public bool DefaultsInScope
        {
            get => _flags.HasFlag(ScopeFlags.DefaultsInScope);
            set => SetFlag(ScopeFlags.DefaultsInScope, value);
        }

        public bool DefaultsInvalid
        {
            get => _flags.HasFlag(ScopeFlags.DefaultsInvalid);
            set => SetFlag(ScopeFlags.DefaultsInvalid, value);
        }

        public bool RequiresRecompose
        {
            get => _flags.HasFlag(ScopeFlags.RequiresRecompose);
            set => SetFlag(ScopeFlags.RequiresRecompose, value);
        }

        public bool Skipped
        {
            get => _flags.HasFlag(ScopeFlags.Skipped);
            private set => SetFlag(ScopeFlags.Skipped, value);
        }

        public bool ForcedRecompose
        {
            get => _flags.HasFlag(ScopeFlags.ForcedRecompose);
            set => SetFlag(ScopeFlags.ForcedRecompose, value);
        }

        public bool CanRecompose => _block != null;

        public bool Valid => _owner != null && _anchor != null && _anchor.Valid;

        public void Compose(IComposerContext composer)
        {
            if (_block != null)
                _block(composer);
        }

        public void Start(int token)
        {
            _currentToken = token;
            Skipped = false;
        }

        public Action? End(int token)
        {
            var trackedInstances = _trackedInstances;
            if (trackedInstances != null && !Skipped)
            {
                return () =>
                {
                };
            }
            return null;
        }

        public void Invalidate()
        {
            _owner?.Invalidate(this, null);
        }

        public InvalidationResult InvalidateForResult(object? value)
        {
            return _owner?.Invalidate(this, value) ?? InvalidationResult.IGNORED;
        }

        public bool IsInvalidFor(object? instances)
        {
            if (instances == null) return true;
            return true;
        }

        public void Release()
        {
            _owner?.RecomposeScopeReleased(this);
            _owner = null;
            _block = null;
        }

        public void AdoptedBy(RecomposeScopeOwner owner)
        {
            _owner = owner;
        }

        public void ScopeSkipped()
        {
            Skipped = true;
        }

        public bool RecordRead(object instance)
        {
            return false;
        }

        public void TrackRead(IStateObject state)
        {
            _owner?.RecordReadOf(state);
        }

        public void UpdateScope(Action<IComposerContext> updater)
        {
            _block = updater;
        }

        private void SetFlag(ScopeFlags flag, bool value)
        {
            if (value)
                _flags |= flag;
            else
                _flags &= ~flag;
        }
    }
}
