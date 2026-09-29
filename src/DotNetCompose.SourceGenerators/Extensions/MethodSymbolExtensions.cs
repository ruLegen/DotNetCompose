using Microsoft.CodeAnalysis;
using System.Collections.Immutable;
using System.Linq;
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

        public static bool IsReadOnlyComposableFunction(this IMethodSymbol? methodSymbol)
            => methodSymbol.GetComposableMode().IsReadOnly();

        public static bool IsReadOnlyComposableParameter(this IParameterSymbol? parameterSymbol)
            => parameterSymbol.GetComposableMode().IsReadOnly();

        public static ComposableModeKind GetComposableMode(this ISymbol? symbol)
        {
            AttributeData? attribute = symbol?.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.GetFullMetadataName() == Consts.ComposableAttributeFullName);
            if (attribute == null || attribute.ConstructorArguments.Length == 0)
                return ComposableModeKind.Restartable;

            TypedConstant argument = attribute.ConstructorArguments[0];
            if (argument.Value is int value)
                return (ComposableModeKind)value;
            return ComposableModeKind.Restartable;
        }

        public static ImmutableArray<MethodParameterInfo> GetParametersInfos(
            this IMethodSymbol method,
            SemanticModel _)
        {
            if (method.Parameters.Length == 0)
                return ImmutableArray<MethodParameterInfo>.Empty;

            using ListPoolObject<MethodParameterInfo> result = ListPool<MethodParameterInfo>.Get();
            ComposableModeKind methodMode = method.GetComposableMode();
            int defaultIdx = 0;
            foreach (IParameterSymbol parameter in method.Parameters)
            {
                bool isComposable = false;
                ImmutableArray<ITypeSymbol> genericArguments = ImmutableArray<ITypeSymbol>.Empty;
                ComposableModeKind parameterMode = ComposableModeKind.Restartable;
                string name = parameter.Name;
                ITypeSymbol? defaultProviderType = null;
                AttributeData? composableAttribute = parameter.GetAttributes()
                    .FirstOrDefault(attribute =>
                        attribute.AttributeClass?.GetFullMetadataName() ==
                        Consts.ComposableAttributeFullName);
                if (composableAttribute != null)
                {
                    isComposable = parameter.Type is INamedTypeSymbol
                    {
                        Name: "Action",
                        TypeKind: TypeKind.Delegate,
                    } actionType && actionType.ContainingNamespace.ToDisplayString() == "System";
                    parameterMode = isComposable
                        ? parameter.GetComposableMode().EffectiveParameterMode(methodMode)
                        : ComposableModeKind.Restartable;
                    if (isComposable && parameter.Type is INamedTypeSymbol
                        {
                            IsGenericType: true,
                        } namedTypeSymbol)
                    {
                        genericArguments = namedTypeSymbol.TypeArguments;
                    }
                }
                AttributeData? defaultAttribute = parameter.GetAttributes()
                    .FirstOrDefault(attribute =>
                        attribute.AttributeClass?.OriginalDefinition?.GetFullMetadataName() ==
                        Consts.DefaultAttributeFullName);
                if (defaultAttribute?.AttributeClass is INamedTypeSymbol namedDefaultAttribute &&
                    namedDefaultAttribute.TypeArguments.Length > 0)
                {
                    defaultProviderType = namedDefaultAttribute.TypeArguments[0];
                }

                int paramDefaultIdx = defaultProviderType != null ? defaultIdx++ : -1;
                result.Add(new MethodParameterInfo(
                    name,
                    isComposable,
                    parameterMode,
                    genericArguments,
                    parameter.Type,
                    defaultProviderType,
                    paramDefaultIdx));
            }

            return ImmutableArray.Create<MethodParameterInfo>(result.ToArray());
        }
    }
}
