using System;
using System.Collections.Generic;
using DotNetCompose.Runtime.Snapshots;

namespace DotNetCompose.Runtime.Composer
{
    internal class RecomposeScopeImpl : IComposeUpdateScope
    {
        private const int UsedFlag = 0x001;
        private const int DefaultsInScopeFlag = 0x002;
        private const int DefaultsInvalidFlag = 0x004;
        private const int RequiresRecomposeFlag = 0x008;
        private const int SkippedFlag = 0x010;
        private const int RereadingFlag = 0x020;
        private const int ForcedRecomposeFlag = 0x040;

        private int _flags;
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
            get => GetFlag(UsedFlag);
            set => SetFlag(UsedFlag, value);
        }

        public bool DefaultsInScope
        {
            get => GetFlag(DefaultsInScopeFlag);
            set => SetFlag(DefaultsInScopeFlag, value);
        }

        public bool DefaultsInvalid
        {
            get => GetFlag(DefaultsInvalidFlag);
            set => SetFlag(DefaultsInvalidFlag, value);
        }

        public bool RequiresRecompose
        {
            get => GetFlag(RequiresRecomposeFlag);
            set => SetFlag(RequiresRecomposeFlag, value);
        }

        public bool Skipped
        {
            get => GetFlag(SkippedFlag);
            private set => SetFlag(SkippedFlag, value);
        }

        public bool ForcedRecompose
        {
            get => GetFlag(ForcedRecomposeFlag);
            set => SetFlag(ForcedRecomposeFlag, value);
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

        private bool GetFlag(int flag) => (_flags & flag) != 0;

        private void SetFlag(int flag, bool value)
        {
            if (value)
                _flags |= flag;
            else
                _flags &= ~flag;
        }
    }
}
