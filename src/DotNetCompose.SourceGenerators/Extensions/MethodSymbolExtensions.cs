using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace DotNetCompose.SourceGenerators.Extensions
{
    public static class MethodSymbolExtensions
    {
        public static bool HasAnyComposablesArguments(this IMethodSymbol? methodSymbol)
        {
            if (methodSymbol == null)
                return false;

            ImmutableArray<ITypeParameterSymbol> parameters = methodSymbol.TypeParameters;
            bool hasAnyComposables = parameters.Any(p => p.DeclaringType?.IsComposableAction() ?? false);
            if(hasAnyComposables)
                return true;

            hasAnyComposables = methodSymbol.Parameters.Any(p=> p.Type.IsComposableAction());
            return hasAnyComposables;
        }
    }
}
