using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotNetCompose.SourceGenerators.Pipeline
{
    internal interface IBodyWrappingStrategy
    {
        BlockSyntax WrapMethodBody(BlockSyntax body, TransformationContext context);
    }
}
