using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

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

        public static bool HasAnyComposablesParamaters(this MethodDeclarationSyntax method, SemanticModel semanticModel)
        {
            return method.ParameterList
                .Parameters
                .Select(p=> semanticModel.GetSymbolInfo(p.Type).Symbol)
                .Where(s=> s != null)
                .Any(s=>s.GetFullMetadataName() == Consts.ComposableActionFullTypeName);
        }
    }
}
