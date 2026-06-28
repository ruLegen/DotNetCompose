namespace DotNetCompose.Runtime.Snapshots
{
    public abstract class StateRecord
    {
        internal long SnapshotId { get; set; } = Snapshots.SnapshotId.Invalid;
        internal StateRecord? Next { get; set; }

        protected StateRecord() { }

        protected StateRecord(long snapshotId)
        {
            SnapshotId = snapshotId;
        }

        public abstract void Assign(StateRecord value);
        public abstract StateRecord Create();

        internal StateRecord Create(long snapshotId)
        {
            var rec = Create();
            rec.SnapshotId = snapshotId;
            return rec;
        }
    }
}
