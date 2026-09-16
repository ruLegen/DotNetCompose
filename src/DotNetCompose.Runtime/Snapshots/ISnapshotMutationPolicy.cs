namespace DotNetCompose.Runtime.Snapshots
{
    public interface ISnapshotMutationPolicy<T>
    {
        bool Equivalent(T a, T b);
        T? Merge(T previous, T current, T applied);
        // Implement this overload for value types: default(T) cannot represent merge failure.
        bool TryMerge(T previous, T current, T applied, out T result)
        {
            T? merged = Merge(previous, current, applied);
            result = merged!;
            return merged is not null;
        }
    }
}
