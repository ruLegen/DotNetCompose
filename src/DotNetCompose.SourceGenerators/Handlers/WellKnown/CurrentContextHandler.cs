using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;

namespace DotNetCompose.SourceGenerators.Handlers.WellKnown
{
    internal sealed class CurrentContextHandler : WellKnownFunctionHandler
    {
        public override ImmutableArray<string> GetMetadataNames()
            => ImmutableArray.Create("DotNetCompose.Runtime.Composables.CurrentContext");

        public override SyntaxNode Handle(
            InvocationExpressionSyntax invocation,
            MethodCallHandlerContext context)
        {
            return SyntaxFactory.IdentifierName(context.Options.ContextVarName);
        }
    }
}
