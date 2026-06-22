using DotNetCompose.SourceGenerators.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static DotNetCompose.SourceGenerators.Extensions.MethodDeclarationSyntaxExtensions;

namespace DotNetCompose.SourceGenerators.Pipeline
{
    internal static class ParameterListTransformer
    {
        internal static ParameterListSyntax ReplaceAllComposableParameters(
            MethodDeclarationSyntax method, SemanticModel semanticModel, bool addAttributeToComposableParameters)
        {
            var methodParams = method.GetParametersInfos(semanticModel);
            return DefaultSignatureRewriter.ReplaceAllComposableParameters(
                method, addAttributeToComposableParameters, semanticModel,
                new MethodGenerationContext(methodParams, false, false));
        }

        internal static ParameterListSyntax AppendComposableContextrelatedParameters(
            ParameterListSyntax paramList, string contextParamName, string changedParamName,string defaultParamName)
        {
            return DefaultSignatureRewriter.AppendComposableContextrelatedParameters(
                paramList, contextParamName, changedParamName, defaultParamName);
        }
    }
}
