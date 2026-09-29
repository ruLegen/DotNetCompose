using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotNetCompose.SourceGenerators.Pipeline
{
    internal interface ISignatureRewriter
    {
        MethodDeclarationSyntax RewriteSignature(MethodDeclarationSyntax method, TransformationContext context);
    }
}
