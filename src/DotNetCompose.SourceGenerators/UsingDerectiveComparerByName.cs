using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;

namespace DotNetCompose.SourceGenerators
{
    public partial class ComposeGenerator
    {
        public class UsingDerectiveComparerByName : IEqualityComparer<UsingDirectiveSyntax>
        {
            public static IEqualityComparer<UsingDirectiveSyntax> Default = new UsingDerectiveComparerByName();
            public bool Equals(UsingDirectiveSyntax x, UsingDirectiveSyntax y)
            {
                return x?.Name?.ToString() == y?.Name?.ToString();
            }

            public int GetHashCode(UsingDirectiveSyntax obj)
            {
                return obj.Name?.GetHashCode() ?? 0;
            }
        }
    }
}
