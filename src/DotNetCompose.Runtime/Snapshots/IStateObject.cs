namespace DotNetCompose.Runtime.Snapshots
{
    public interface IStateObject
    {
        StateRecord FirstStateRecord { get; }
        void PrependStateRecord(StateRecord value);
        StateRecord? MergeRecords(StateRecord previous, StateRecord current, StateRecord applied);
    }
}
