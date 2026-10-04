
using DotNetCompose.SourceGenerators.Handlers;
using Microsoft.CodeAnalysis;
using System.Collections.Generic;

namespace DotNetCompose.SourceGenerators.Pipeline
{
    internal sealed record PipelineContext(
        StrategyContainer Strategies,
        IReadOnlyList<IMethodCallHandler> MethodCallHandlers,
        WellKnownFunctionRegistry WellKnownRegistry,
        bool GenerateDiagnostics,
        string ProjectDirectory,
        bool SupportsEnhancedLineDirectives,
        bool? UseStackAllocForArgumentStates
    );

    internal interface IOutputHandler
    {
        void Handle(SourceProductionContext spc, Compilation compilation, ClassAndComposablesMethods input, PipelineContext context);
    }
}
