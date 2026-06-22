using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;

namespace DotNetCompose.SourceGenerators.Handlers
{
    internal abstract class WellKnownFunctionHandler
    {
        public abstract ImmutableArray<string> GetMetadataNames();

        public abstract SyntaxNode? Handle(
            InvocationExpressionSyntax invocation,
            MethodCallHandlerContext context);
    }
}
