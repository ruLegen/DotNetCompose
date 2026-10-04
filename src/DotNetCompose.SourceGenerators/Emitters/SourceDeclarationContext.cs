using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;
using System.Linq;

namespace DotNetCompose.SourceGenerators.Emitters
{
    // Imports belong to lexical scopes, not to the combined partial type.
    internal sealed record SourceDeclarationContext(
        ImmutableArray<UsingDirectiveSyntax> FileUsings,
        ImmutableArray<ExternAliasDirectiveSyntax> FileExterns,
        ImmutableArray<SourceNamespaceScope> Namespaces)
    {
        public static SourceDeclarationContext Create(TypeDeclarationSyntax declaration)
        {
            var root = (CompilationUnitSyntax)declaration.SyntaxTree.GetRoot();
            return new SourceDeclarationContext(
                root.Usings.Where(directive => directive.GlobalKeyword.RawKind == 0).ToImmutableArray(),
                root.Externs.ToImmutableArray(),
                declaration.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().Reverse()
                    .Select(scope => new SourceNamespaceScope(
                        scope.Name.WithoutTrivia().ToFullString(),
                        scope.Usings.ToImmutableArray(),
                        scope.Externs.ToImmutableArray()))
                    .ToImmutableArray());
        }
    }

    internal sealed record SourceNamespaceScope(
        string Name,
        ImmutableArray<UsingDirectiveSyntax> Usings,
        ImmutableArray<ExternAliasDirectiveSyntax> Externs);
}
