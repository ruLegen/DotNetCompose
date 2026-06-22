using DotNetCompose.SourceGenerators.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;

namespace DotNetCompose.SourceGenerators.Handlers
{
    internal sealed class MethodCallHandlerContext
    {
        public MethodCallHandlerContext(
            SemanticModel semanticModel,
            RewriterOptions options,
            MethodGenerationContext methodCtx,
            RewriterSession session,
            IDiagnosticReporter diagnostics,
            Func<SyntaxNode, SyntaxNode> visitNode)
        {
            SemanticModel = semanticModel;
            Options = options;
            MethodCtx = methodCtx;
            Session = session;
            Diagnostics = diagnostics;
            VisitNode = visitNode;
        }

        public SemanticModel SemanticModel { get; }
        public RewriterOptions Options { get; }
        public MethodGenerationContext MethodCtx { get; }
        public RewriterSession Session { get; }
        public IDiagnosticReporter Diagnostics { get; }
        public Func<SyntaxNode, SyntaxNode> VisitNode { get; }
    }

    internal interface IMethodCallHandler
    {
        bool TryHandle(
            ExpressionSyntax expression,
            IMethodSymbol methodSymbol,
            MethodCallHandlerContext context,
            out SyntaxNode replacement);
    }
}
