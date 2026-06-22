using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotNetCompose.SourceGenerators.Pipeline
{
    internal interface IParameterChangedTransformer
    {
        BlockSyntax TransformParameters(BlockSyntax body, TransformationContext context);
    }
}
