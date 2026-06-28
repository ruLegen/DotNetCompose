namespace DotNetCompose.Runtime.Snapshots
{
    public interface ISnapshotMutationPolicy<T>
    {
        bool Equivalent(T a, T b);
        T? Merge(T previous, T current, T applied);
    }
}
