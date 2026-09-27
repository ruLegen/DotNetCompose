using DotNetCompose.SourceGenerators.Extensions;
using Microsoft.CodeAnalysis;
using System.Linq;

namespace DotNetCompose.SourceGenerators.Helpers
{
    internal static class DefaultProviderResolver
    {
        internal static IMethodSymbol? Resolve(
            ITypeSymbol providerType,
            ITypeSymbol valueType,
            SemanticModel semanticModel,
            int position,
            bool readOnly)
        {
            IMethodSymbol[] candidates = providerType.GetMembers("Create")
                .OfType<IMethodSymbol>()
                .Where(method => method.IsStatic && method.Arity == 0 && method.Parameters.Length == 0 &&
                    !method.ReturnsVoid && !method.ReturnsByRef && !method.ReturnsByRefReadonly &&
                    semanticModel.IsAccessible(position, method) &&
                    semanticModel.Compilation.ClassifyCommonConversion(method.ReturnType, valueType).IsImplicit &&
                    (!method.IsComposableFunction() ||
                        (!readOnly && method.GetComposableMode() == ComposableModeKind.Inline)))
                .ToArray();
            return candidates.Length == 1 ? candidates[0] : null;
        }
    }
}
