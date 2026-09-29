using System;

namespace DotNetCompose.Runtime.Snapshots
{
    internal static class SnapshotMutableStateFactory
    {
        public static SnapshotMutableState<T> Create<T>(T value, ISnapshotMutationPolicy<T>? policy)
        {
            policy ??= (typeof(T).IsValueType ? StructuralPolicy<T>.Default : ReferentialPolicy<T>.Default);
            return new SnapshotMutableState<T>(value, policy);
        }
    }
}
