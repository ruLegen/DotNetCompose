using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Diagnostics.CodeAnalysis;

namespace DotNetCompose.SourceGenerators.Handlers
{
    internal enum InterceptionResult
    {
        Continue,
        Handled,
    }

    internal class MethodCallHandlerContext
    {
        public SemanticModel SemanticModel { get; set; }
        public RewriterOptions Options { get; set; }
        public MethodGenerationContext MethodCtx { get; set; }
        public RewriterSession Session { get; set; }
        public Func<SyntaxNode, SyntaxNode> VisitNode { get; set; }
    }

    internal interface IMethodCallHandler
    {
        InterceptionResult Handle(
            ExpressionSyntax expression,
            IMethodSymbol methodSymbol,
            MethodCallHandlerContext context,
            out SyntaxNode replacement);
    }
}
