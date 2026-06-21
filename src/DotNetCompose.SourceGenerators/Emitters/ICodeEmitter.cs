using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;

namespace DotNetCompose.SourceGenerators.Emitters
{
    internal record CodeGenerationInput(
        string Namespace,
        string TypeName,
        string Accessibility,
        ImmutableArray<UsingDirectiveSyntax> Usings,
        ImmutableArray<SyntaxNode> BuilderMethods,
        ImmutableArray<RewriterSession> Sessions);

    internal interface ICodeEmitter
    {
        string Emit(CodeGenerationInput input);
    }
}
