using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using static DotNetCompose.SourceGenerators.Extensions.MethodDeclarationSyntaxExtensions;

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
            if (hasAnyComposables)
                return true;

            hasAnyComposables = methodSymbol.Parameters.Any(p => p.Type.IsComposableAction());
            return hasAnyComposables;
        }

        public static bool IsComposableFunction(this IMethodSymbol? methodSymbol)
        {
            if (methodSymbol == null)
                return false;

            ImmutableArray<AttributeData> attributes = methodSymbol.GetAttributes();
            bool isComposable = attributes.Any(a => a.AttributeClass?.GetFullMetadataName() == Consts.ComposableAttributeFullName);
            return isComposable;
        }

        public static ImmutableArray<MethodParameterInfo> GetParametersInfos(this IMethodSymbol method, SemanticModel semanticModel)
        {
            if (method.Parameters.Length == 0)
                return ImmutableArray<MethodParameterInfo>.Empty;

            if(method.MethodKind == MethodKind.Ordinary)
            {

            }else if(method.MethodKind == MethodKind.DelegateInvoke)
            {

            }
                //.Select(p => (p.Name, p.Type.IsComposableAction()))
                using ListPoolObject<MethodParameterInfo> result = ListPool<MethodParameterInfo>.Get();
            ImmutableArray<ITypeSymbol> genericArguments = ImmutableArray<ITypeSymbol>.Empty;
            int defaultIdx = 0;
            foreach (IParameterSymbol parameter in method.Parameters)
            {
                bool isComposable = false;
                string name = parameter.Name;
                ITypeSymbol? defaultProviderType = null;
                AttributeData? composableAttribute = parameter.GetAttributes()
                                                .FirstOrDefault(attribData => attribData.AttributeClass.GetFullMetadataName() == Consts.ComposableAttributeFullName);
                if (composableAttribute != null)
                {
                    ISymbol? symbol = parameter.Type;
                    isComposable = symbol != null && symbol.GetFullMetadataName().Contains("System.Action");
                    if (isComposable && symbol is INamedTypeSymbol namedTypeSymbol && namedTypeSymbol.IsGenericType)
                    {
                        genericArguments = namedTypeSymbol.TypeArguments;
                    }
                }
                AttributeData? defaultAttribute = parameter.GetAttributes()
                    .FirstOrDefault(attribData => attribData.AttributeClass?.OriginalDefinition?.GetFullMetadataName() == Consts.DefaultAttributeFullName);
                if (defaultAttribute != null && defaultAttribute.AttributeClass is INamedTypeSymbol namedDefaultAttr && namedDefaultAttr.TypeArguments.Length > 0)
                {
                    defaultProviderType = namedDefaultAttr.TypeArguments[0];
                }
                int paramDefaultIdx = defaultProviderType != null ? defaultIdx++ : -1;
                result.Add(new MethodParameterInfo(name, isComposable, genericArguments, parameter.Type, defaultProviderType, paramDefaultIdx));
            }
            return ImmutableArray.Create<MethodParameterInfo>(result.ToArray());
        }
    }
}
