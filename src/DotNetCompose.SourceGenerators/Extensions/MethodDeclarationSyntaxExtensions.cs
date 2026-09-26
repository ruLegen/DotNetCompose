using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;
using System.Linq;

namespace DotNetCompose.SourceGenerators.Extensions
{
    public static class MethodDeclarationSyntaxExtensions
    {
        public static string GetMethodID(this MethodDeclarationSyntax method, SemanticModel? semanticModel)
        {
            ISymbol? methodSymbol = semanticModel?.GetDeclaredSymbol(method);
            string methodId = methodSymbol?.GetFullMetadataName()
                           ?? method.Identifier.ValueText;
            return methodId;
        }

        public static string GetFullTypeName(this MethodDeclarationSyntax method, Compilation compilation)
        {
            var semanticModel = compilation.GetSemanticModel(method.SyntaxTree);
            var methodSymbol = semanticModel.GetDeclaredSymbol(method);
            return methodSymbol?.ContainingType?.ToDisplayString() ?? string.Empty;
        }
        public record MethodParameterInfo(
            string Name,
            bool IsComposable,
            ComposableModeKind Mode,
            ImmutableArray<ITypeSymbol> GenericArguments,
            ITypeSymbol? Type = null,
            ITypeSymbol? DefaultProviderType = null,
            int DefaultIndex = -1)
        {
            public bool IsReadOnly => Mode.IsReadOnly();
            public bool IsInline => Mode.IsInline();
        }

        public static bool HasAnyComposablesParamaters(this MethodDeclarationSyntax method, SemanticModel semanticModel)
        {
            var r = method.ParameterList
                .Parameters
                .Where(p => p.AttributeLists.Count > 0)
                .Where(p => p.AttributeLists.Any(aList => aList.Attributes.Any(a => IsComposableAttribute(a, semanticModel))))
                .Select(p => semanticModel.GetSymbolInfo(p.Type).Symbol)
                .Where(s => s != null)
                .Any(s => s.GetFullMetadataName().Contains("System.Action"));
            return r;
        }

        public static ImmutableArray<MethodParameterInfo> GetParametersInfos(
            this MethodDeclarationSyntax method,
            SemanticModel semanticModel)
        {
            if (method.ParameterList.Parameters.Count == 0)
                return ImmutableArray<MethodParameterInfo>.Empty;

            using ListPoolObject<MethodParameterInfo> result = ListPool<MethodParameterInfo>.Get();
            IMethodSymbol? methodSymbol = semanticModel.GetDeclaredSymbol(method) as IMethodSymbol;
            ComposableModeKind methodMode = methodSymbol.GetComposableMode();
            int defaultIdx = 0;
            foreach (ParameterSyntax parameter in method.ParameterList.Parameters)
            {
                bool isComposable = false;
                ImmutableArray<ITypeSymbol> genericArguments = ImmutableArray<ITypeSymbol>.Empty;
                ComposableModeKind parameterMode = ComposableModeKind.Restartable;
                IParameterSymbol? parameterSymbol = semanticModel.GetDeclaredSymbol(parameter) as IParameterSymbol;
                string name = parameterSymbol?.Name ?? string.Empty;
                ITypeSymbol? paramType = parameterSymbol?.Type;
                ITypeSymbol? defaultProviderType = null;
                AttributeSyntax? composableAttribute = parameter.AttributeLists
                    .SelectMany(aList => aList.Attributes)
                    .FirstOrDefault(a => IsComposableAttribute(a, semanticModel));
                if (composableAttribute != null)
                {
                    isComposable = parameterSymbol?.Type is INamedTypeSymbol
                    {
                        Name: "Action",
                        TypeKind: TypeKind.Delegate,
                    } actionType && actionType.ContainingNamespace.ToDisplayString() == "System";
                    parameterMode = isComposable
                        ? parameterSymbol.GetComposableMode().EffectiveParameterMode(methodMode)
                        : ComposableModeKind.Restartable;
                    if (isComposable && parameterSymbol?.Type is INamedTypeSymbol
                        {
                            IsGenericType: true,
                        } namedTypeSymbol)
                    {
                        genericArguments = namedTypeSymbol.TypeArguments;
                    }
                }
                foreach (var attributeList in parameter.AttributeLists)
                {
                    foreach (var attribute in attributeList.Attributes)
                    {
                        string? attrName = (attribute.Name as SimpleNameSyntax)?.Identifier.ValueText;
                        bool isDefaultAttr = attrName == "Default" || attrName == "DefaultAttribute";
                        if (isDefaultAttr && attribute.Name is GenericNameSyntax genericName &&
                            genericName.TypeArgumentList.Arguments.Count > 0)
                        {
                            var typeArg = genericName.TypeArgumentList.Arguments[0];
                            defaultProviderType = semanticModel.GetTypeInfo(typeArg).Type;
                        }
                    }
                }

                int paramDefaultIdx = defaultProviderType != null ? defaultIdx++ : -1;
                result.Add(new MethodParameterInfo(
                    name,
                    isComposable,
                    parameterMode,
                    genericArguments,
                    paramType,
                    defaultProviderType,
                    paramDefaultIdx));
            }

            return ImmutableArray.Create<MethodParameterInfo>(result.ToArray());
        }

        public static bool IsComposableAttribute(AttributeSyntax s, SemanticModel semanticModel)
        {
            var symbol = semanticModel.GetSymbolInfo(s).Symbol;
            if (symbol == null)
                return false;
            if (symbol.Kind == SymbolKind.Method)
                symbol = symbol.ContainingSymbol;

            return symbol.GetFullMetadataName() == Consts.ComposableAttributeFullName;
        }
    }
}
