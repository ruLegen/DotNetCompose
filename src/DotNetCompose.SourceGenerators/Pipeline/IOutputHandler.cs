using DotNetCompose.SourceGenerators.Handlers;
using Microsoft.CodeAnalysis;
using System.Collections.Generic;

namespace DotNetCompose.SourceGenerators.Pipeline
{
    internal sealed record PipelineContext(
        StrategyContainer Strategies,
        IReadOnlyList<IMethodCallHandler> MethodCallHandlers,
        WellKnownFunctionRegistry WellKnownRegistry
    );

    internal interface IOutputHandler
    {
        void Handle(SourceProductionContext spc, Compilation compilation, ClassAndComposablesMethods input, PipelineContext context);
    }
}
