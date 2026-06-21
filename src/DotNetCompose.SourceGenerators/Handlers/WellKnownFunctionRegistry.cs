using DotNetCompose.SourceGenerators.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace DotNetCompose.SourceGenerators.Handlers
{
    internal sealed class WellKnownFunctionRegistry
    {
        public static readonly WellKnownFunctionRegistry Empty = new WellKnownFunctionRegistry(
            new Dictionary<string, WellKnownHandler>(0));

        private readonly Dictionary<string, WellKnownHandler> _handlers;

        private WellKnownFunctionRegistry(Dictionary<string, WellKnownHandler> handlers)
            => _handlers = handlers;

        public bool TryHandle(
            IMethodSymbol symbol,
            InvocationExpressionSyntax invocation,
            MethodCallHandlerContext context,
            out SyntaxNode? replacement)
        {
            var key = symbol.GetFullMetadataName();
            if (_handlers.TryGetValue(key, out var handler))
            {
                replacement = handler.Handle(invocation, context);
                return true;
            }
            replacement = null;
            return false;
        }

        internal sealed class Builder
        {
            private readonly Dictionary<string, WellKnownHandler> _handlers = new();

            public Builder Register<T>() where T : WellKnownHandler, new()
            {
                var handler = new T();
                foreach (var name in handler.GetMetadataNames())
                    _handlers[name] = handler;
                return this;
            }

            public WellKnownFunctionRegistry Build() => new(_handlers);
        }
    }
}
