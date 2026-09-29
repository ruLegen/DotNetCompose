using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;

namespace DotNetCompose.SourceGenerators
{
    internal sealed record MethodFullNameAndDeclaration(string FullName, MethodDeclarationSyntax? Declaration, int ContentHash)
        : IEquatable<MethodFullNameAndDeclaration>
    {
        public bool Equals(MethodFullNameAndDeclaration? other)
            => other is not null && FullName == other.FullName && ContentHash == other.ContentHash;
        public override int GetHashCode() => (FullName, ContentHash).GetHashCode();
    }
}
