using System.Collections.Generic;

namespace DotNetCompose.Runtime.Snapshots
{
    public class StructuralPolicy<T> : ISnapshotMutationPolicy<T>
    {
        public static readonly StructuralPolicy<T> Default = new();

        public bool Equivalent(T a, T b)
        {
            if (a == null) return b == null;
            if (b == null) return false;
            return EqualityComparer<T>.Default.Equals(a, b);
        }

        public T? Merge(T previous, T current, T applied) => default;
    }

    public class ReferentialPolicy<T> : ISnapshotMutationPolicy<T>
    {
        public static readonly ReferentialPolicy<T> Default = new();

        public bool Equivalent(T a, T b) => ReferenceEquals(a, b);

        public T? Merge(T previous, T current, T applied) => default;
    }

    public class NeverEqualPolicy<T> : ISnapshotMutationPolicy<T>
    {
        public static readonly NeverEqualPolicy<T> Default = new();

        public bool Equivalent(T a, T b) => false;

        public T? Merge(T previous, T current, T applied) => default;
    }
}
