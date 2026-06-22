using DotNetCompose.SourceGenerators.Diagnostics;
using DotNetCompose.SourceGenerators.Handlers;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;

namespace DotNetCompose.SourceGenerators.Pipeline
{
    internal sealed class TransformationContext
    {
        private Func<SyntaxNode, SyntaxNode> _visitNode;

        public TransformationContext(
            SemanticModel semanticModel,
            RewriterOptions options,
            MethodGenerationContext methodCtx,
            RewriterSession session,
            IReadOnlyList<IMethodCallHandler> methodCallHandlers,
            WellKnownFunctionRegistry wellKnownRegistry,
            Func<SyntaxNode, SyntaxNode> visitNode)
        {
            SemanticModel = semanticModel;
            Options = options;
            MethodCtx = methodCtx;
            Session = session;
            MethodCallHandlers = methodCallHandlers;
            WellKnownRegistry = wellKnownRegistry;
            _visitNode = visitNode;
        }

        public SemanticModel SemanticModel { get; }
        public RewriterOptions Options { get; }
        public MethodGenerationContext MethodCtx { get; }
        public RewriterSession Session { get; }
        public IReadOnlyList<IMethodCallHandler> MethodCallHandlers { get; }
        public WellKnownFunctionRegistry WellKnownRegistry { get; }
        public Func<SyntaxNode, SyntaxNode> VisitNode => _visitNode;
        public IDiagnosticReporter Diagnostics => Session.Diagnostics;

        public void SetVisitNode(Func<SyntaxNode, SyntaxNode> visitNode)
        {
            _visitNode = visitNode;
        }
    }
}
