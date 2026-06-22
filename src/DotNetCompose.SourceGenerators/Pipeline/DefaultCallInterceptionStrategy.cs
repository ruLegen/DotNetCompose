using DotNetCompose.SourceGenerators.Handlers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotNetCompose.SourceGenerators.Pipeline
{
    internal sealed class DefaultCallInterceptionStrategy : ICallInterceptionStrategy
    {
        public SyntaxNode? TryInterceptInvocation(InvocationExpressionSyntax node, TransformationContext context)
        {
            IMethodSymbol? methodSymbol = context.SemanticModel.GetSymbolInfo(node).Symbol as IMethodSymbol;
            if (methodSymbol == null)
                return null;

            MethodCallHandlerContext handlerContext = new MethodCallHandlerContext(
                context.SemanticModel,
                context.Options,
                context.MethodCtx,
                context.Session,
                context.Diagnostics,
                context.VisitNode);

            if (context.WellKnownRegistry.TryHandle(methodSymbol, node, handlerContext, out var wellKnownReplacement))
                return wellKnownReplacement;

            foreach (IMethodCallHandler handler in context.MethodCallHandlers)
            {
                if (handler.TryHandle(node, methodSymbol, handlerContext, out SyntaxNode? replacement))
                    return replacement;
            }

            return null;
        }

        public SyntaxNode? TryInterceptConditionalAccess(ConditionalAccessExpressionSyntax node, TransformationContext context)
        {
            IMethodSymbol? methodSymbol = context.SemanticModel.GetSymbolInfo(node.WhenNotNull).Symbol as IMethodSymbol;
            if (methodSymbol == null)
                return null;

            MethodCallHandlerContext handlerContext = new MethodCallHandlerContext(
                context.SemanticModel,
                context.Options,
                context.MethodCtx,
                context.Session,
                context.Diagnostics,
                context.VisitNode);

            foreach (IMethodCallHandler handler in context.MethodCallHandlers)
            {
                if (handler.TryHandle(node, methodSymbol, handlerContext, out SyntaxNode? replacement))
                    return replacement;
            }

            return null;
        }
    }
}
