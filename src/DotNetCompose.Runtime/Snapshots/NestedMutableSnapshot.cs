using System;
using System.Collections.Generic;

namespace DotNetCompose.Runtime.Snapshots
{
    public class NestedMutableSnapshot : MutableSnapshot
    {
        private readonly MutableSnapshot _parent;
        private bool _deactivated;

        public override Snapshot Root => _parent.Root;

        internal NestedMutableSnapshot(
            long id, SnapshotIdSet invalid,
            Action<object>? readObserver,
            Action<object>? writeObserver,
            MutableSnapshot parent)
            : base(id, invalid, readObserver, writeObserver)
        {
            _parent = parent;
            parent.ActivateNestedSnapshot();
        }

        public override SnapshotApplyResult Apply()
        {
            var modified = Modified;
            if (modified == null || modified.Count == 0)
            {
                using (Snapshot.Lock()) CloseLocked();
                Applied = true;
                return SnapshotApplyResult.Success;
            }

            SnapshotApplyResult mergeResult;
            using (Snapshot.Lock())
            {
                mergeResult = InnerApplyLocked(this, modified, _parent.Invalid);
                if (mergeResult.Succeeded)
                {
                    CloseLocked();
                    _parent.Invalid = _parent.Invalid.Clear(Id).AndNot(PreviousIds);
                    _parent.RecordPrevious(Id);
                    foreach (var state in modified)
                        _parent.RecordModified(state);
                }
            }

            if (mergeResult.Succeeded)
            {
                Applied = true;
                Modified = null;
            }

            return mergeResult;
        }

        private void RecordModified(HashSet<IStateObject> states)
        {
            foreach (var state in states)
                _parent.RecordModified(state);
        }

        public override void Dispose()
        {
            if (!Disposed)
            {
                base.Dispose();
                Deactivate();
            }
        }

        private void Deactivate()
        {
            if (!_deactivated)
            {
                _deactivated = true;
                _parent.NestedDeactivated(this);
            }
        }
    }
}
