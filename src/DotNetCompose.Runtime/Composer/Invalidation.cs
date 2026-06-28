using System.Collections.Generic;

namespace DotNetCompose.Runtime.Composer
{
    internal class Invalidation
    {
        public RecomposeScopeImpl Scope { get; }
        public int Location { get; set; }
        public object? Instances { get; set; }

        public Invalidation(RecomposeScopeImpl scope, int location, object? instances)
        {
            Scope = scope;
            Location = location;
            Instances = instances;
        }

        public bool IsInvalid() => Scope.IsInvalidFor(Instances);
    }

    internal class InvalidationLocationAscending : Comparer<Invalidation>
    {
        public static readonly InvalidationLocationAscending Instance = new();
        public override int Compare(Invalidation? a, Invalidation? b)
        {
            if (a == null && b == null) return 0;
            if (a == null) return -1;
            if (b == null) return 1;
            return a.Location.CompareTo(b.Location);
        }
    }
}
