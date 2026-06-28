using System;

namespace DotNetCompose.Runtime.Snapshots
{
    internal static class SnapshotMutableStateFactory
    {
        public static SnapshotMutableState<T> Create<T>(T value, ISnapshotMutationPolicy<T>? policy)
        {
            policy ??= StructuralEqualityPolicy<T>.Default;
            return new SnapshotMutableState<T>(value, policy);
        }
    }
}
