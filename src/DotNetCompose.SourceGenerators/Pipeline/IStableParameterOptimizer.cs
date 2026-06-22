using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotNetCompose.SourceGenerators.Pipeline
{
    internal interface IStableParameterOptimizer
    {
        BlockSyntax OptimizeStableParameters(BlockSyntax body, TransformationContext context);
    }
}
