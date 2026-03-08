using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
#nullable enable
namespace System.Runtime.CompilerServices
{
}
namespace DotNetCompose.SourceGenerators.Extensions
{

    public static class MethodDeclarationSyntaxExtensions
    {
        public static string GetFullTypeName(this MethodDeclarationSyntax method, Compilation compilation)
        {
            var semanticModel = compilation.GetSemanticModel(method.SyntaxTree);
            var methodSymbol = semanticModel.GetDeclaredSymbol(method);
            return methodSymbol?.ContainingType?.ToDisplayString() ?? string.Empty;
        }
        public record MethodParameterInfo(string Name, bool IsComposable, ImmutableArray<ITypeSymbol> GenericArguments);
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

        public static ImmutableArray<MethodParameterInfo> GetParametersInfos(this MethodDeclarationSyntax method, SemanticModel semanticModel)
        {
            if (method.ParameterList.Parameters.Count == 0)
                return ImmutableArray<MethodParameterInfo>.Empty;
            using ListPoolObject<MethodParameterInfo> result = ListPool<MethodParameterInfo>.Get();
            ImmutableArray<ITypeSymbol> genericArguments = ImmutableArray<ITypeSymbol>.Empty;
            foreach (ParameterSyntax parameter in method.ParameterList.Parameters)
            {
                bool isComposable = false;
                string name = semanticModel.GetDeclaredSymbol(parameter)?.Name ?? string.Empty;
                if (parameter.AttributeLists.Any(aList => aList.Attributes.Any(a => IsComposableAttribute(a, semanticModel))))
                {
                    ISymbol? symbol = semanticModel.GetSymbolInfo(parameter.Type).Symbol;
                    isComposable =symbol != null && symbol.GetFullMetadataName().Contains("System.Action");
                    if (isComposable && symbol is INamedTypeSymbol namedTypeSymbol && namedTypeSymbol.IsGenericType)
                    {
                        genericArguments = namedTypeSymbol.TypeArguments;
                    }
                }
                result.Add(new MethodParameterInfo(name,isComposable, genericArguments));
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
