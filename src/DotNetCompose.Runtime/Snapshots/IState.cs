namespace DotNetCompose.Runtime.Snapshots
{
    public interface IState<T>
    {
        T Value { get; }
    }

    public interface IMutableState<T> : IState<T>
    {
        new T Value { get; set; }
    }

    public interface ISnapshotMutableState<T> : IMutableState<T>
    {
        ISnapshotMutationPolicy<T> Policy { get; }
    }
}
