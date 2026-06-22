using DotNetCompose.SourceGenerators.Diagnostics;
using DotNetCompose.SourceGenerators.Handlers;
using Microsoft.CodeAnalysis;
using System.Collections.Generic;

namespace DotNetCompose.SourceGenerators.Pipeline
{
    internal sealed class TransformationContext
    {
        public TransformationContext(
            SemanticModel semanticModel,
            RewriterOptions options,
            MethodGenerationContext methodCtx,
            RewriterSession session,
            IReadOnlyList<IMethodCallHandler> methodCallHandlers,
            WellKnownFunctionRegistry wellKnownRegistry,
            NodeTransformer visitNode,
            StrategyContainer strategies)
        {
            SemanticModel = semanticModel;
            Options = options;
            MethodCtx = methodCtx;
            Session = session;
            MethodCallHandlers = methodCallHandlers;
            WellKnownRegistry = wellKnownRegistry;
            NodeTransformer = visitNode;
            Strategies = strategies;
        }

        public SemanticModel SemanticModel { get; }
        public RewriterOptions Options { get; }
        public MethodGenerationContext MethodCtx { get; }
        public RewriterSession Session { get; }
        public IReadOnlyList<IMethodCallHandler> MethodCallHandlers { get; }
        public WellKnownFunctionRegistry WellKnownRegistry { get; }
        public NodeTransformer NodeTransformer { get; }
        public IDiagnosticReporter Diagnostics => Session.Diagnostics;
        public StrategyContainer Strategies { get; }
    }
}
