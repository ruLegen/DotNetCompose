using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotNetCompose.SourceGenerators.Pipeline
{
    internal interface IDefaultValueSubstitutor
    {
        BlockSyntax SubstituteDefaults(BlockSyntax body, TransformationContext context);
    }
}
