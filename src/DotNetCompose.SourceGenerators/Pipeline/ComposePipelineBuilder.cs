using DotNetCompose.SourceGenerators.Handlers;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace DotNetCompose.SourceGenerators.Pipeline
{
    internal sealed class ComposePipelineBuilder 
    {
        private StrategyContainer _strategies = StrategyContainer.Default;
        private readonly List<Func<IMethodCallHandler>> _handlerFactories = new();
        private readonly List<Func<WellKnownFunctionHandler>> _wellKnownHandlerFactories = new();
        private readonly List<IOutputHandler> _outputHandlers = new();

        public ComposePipelineBuilder SetStrategies(StrategyContainer strategies)
        {
            _strategies = strategies;
            return this;
        }

        public ComposePipelineBuilder AddMethodCallHandler<T>() where T : IMethodCallHandler, new()
        {
            _handlerFactories.Add(() => new T());
            return this;
        }

        public ComposePipelineBuilder AddWellKnownHandler<T>() where T : WellKnownFunctionHandler, new()
        {
            _wellKnownHandlerFactories.Add(() => new T());
            return this;
        }

        public ComposePipelineBuilder AddOutput(IOutputHandler handler)
        {
            _outputHandlers.Add(handler);
            return this;
        }

        public IComposePipeline Build()
        {
            var handlers = _handlerFactories.ConvertAll(f => f()).AsReadOnly();
            var wellKnownRegistry = BuildWellKnownRegistry();
            var context = new PipelineContext(_strategies, handlers, wellKnownRegistry);
            var outputHandlers = _outputHandlers.ToImmutableArray();
            return new PipelineInstance(context, outputHandlers);
        }

        private WellKnownFunctionRegistry BuildWellKnownRegistry()
        {
            if (_wellKnownHandlerFactories.Count == 0)
                return WellKnownFunctionRegistry.Empty;

            var builder = WellKnownFunctionRegistry.EmptyBuilder;
            foreach (var factory in _wellKnownHandlerFactories)
                builder.Register(factory());
            return builder.Build();
        }

        
        private sealed class PipelineInstance : IComposePipeline
        {
            private readonly ImmutableArray<IOutputHandler> _outputHandlers;

            public PipelineInstance(
                PipelineContext context,
                ImmutableArray<IOutputHandler> outputHandlers)
            {
                Context = context;
                _outputHandlers = outputHandlers;
            }

            public PipelineContext Context { get; }

            public void Execute(SourceProductionContext spc, Compilation compilation, ClassAndComposablesMethods input)
            {
                foreach (IOutputHandler handler in _outputHandlers)
                    handler.Handle(spc, compilation, input, Context);
            }
        }
    }

    internal interface IComposePipeline
    {
        PipelineContext Context { get; }
        void Execute(SourceProductionContext spc, Compilation compilation, ClassAndComposablesMethods input);
    }
}
