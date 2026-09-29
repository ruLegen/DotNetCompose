using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotNetCompose.SourceGenerators.Pipeline
{
    internal interface ICallInterceptionStrategy
    {
        SyntaxNode? TryInterceptInvocation(InvocationExpressionSyntax invocation, TransformationContext context);
        SyntaxNode? TryInterceptConditionalAccess(ConditionalAccessExpressionSyntax conditionalAccess, TransformationContext context);
    }
}
