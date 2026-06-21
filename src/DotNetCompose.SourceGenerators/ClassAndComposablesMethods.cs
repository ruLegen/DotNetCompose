using System;
using System.Collections.Immutable;
using System.Linq;

namespace DotNetCompose.SourceGenerators
{
    internal sealed record ClassAndComposablesMethods(
        string ClassName,
        ImmutableArray<MethodFullNameAndDeclaration> Methods,
        bool IsPartial)
        : IEquatable<ClassAndComposablesMethods>
    {
        public bool Equals(ClassAndComposablesMethods? other)
            => other is not null && ClassName == other.ClassName
                && IsPartial == other.IsPartial
                && Methods.SequenceEqual(other.Methods);
        public override int GetHashCode() => ClassName?.GetHashCode() ?? 0;
    }
}
