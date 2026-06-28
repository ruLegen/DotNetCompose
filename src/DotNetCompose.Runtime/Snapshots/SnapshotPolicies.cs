using System.Collections.Generic;

namespace DotNetCompose.Runtime.Snapshots
{
    public class StructuralEqualityPolicy<T> : ISnapshotMutationPolicy<T>
    {
        public static readonly StructuralEqualityPolicy<T> Default = new();

        public bool Equivalent(T a, T b)
        {
            if (a == null) return b == null;
            if (b == null) return false;
            return EqualityComparer<T>.Default.Equals(a, b);
        }

        public T? Merge(T previous, T current, T applied) => default;
    }

    public class ReferentialEqualityPolicy<T> : ISnapshotMutationPolicy<T>
        where T : class
    {
        public static readonly ReferentialEqualityPolicy<T> Instance = new();

        public bool Equivalent(T a, T b) => ReferenceEquals(a, b);

        public T? Merge(T previous, T current, T applied) => default;
    }

    public class NeverEqualPolicy<T> : ISnapshotMutationPolicy<T>
    {
        public static readonly NeverEqualPolicy<T> Instance = new();

        public bool Equivalent(T a, T b) => false;

        public T? Merge(T previous, T current, T applied) => default;
    }
}
