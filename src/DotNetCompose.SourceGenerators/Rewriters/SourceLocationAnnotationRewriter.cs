using DotNetCompose.SourceGenerators.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;

namespace DotNetCompose.SourceGenerators.Rewriters
{
    // Ordinary callbacks are copied without composition lowering. Only their
    // original source locations need to survive normalization and emission.
    internal sealed class SourceLocationAnnotationRewriter : CSharpSyntaxRewriter
    {
        public override SyntaxNode? Visit(SyntaxNode? node)
        {
            SyntaxNode? processed = base.Visit(node);
            if (node == null || processed == null)
                return processed;

            bool mapsStatement = node is ExpressionStatementSyntax or VariableDeclarationSyntax or
                IfStatementSyntax or ForStatementSyntax or ForEachStatementSyntax or
                WhileStatementSyntax or DoStatementSyntax or SwitchStatementSyntax or
                ReturnStatementSyntax or ThrowStatementSyntax;
            bool mapsLambdaBody = node is ExpressionSyntax or BlockSyntax &&
                node.Parent is AnonymousFunctionExpressionSyntax;
            return mapsStatement || mapsLambdaBody ? Annotate(node, processed) : processed;
        }

        internal static T Annotate<T>(SyntaxNode source, T processed) where T : SyntaxNode
        {
            if (processed.GetAnnotations("location").Any())
                return processed;

            SyntaxAnnotation? annotation = source.CreateLocationSyntaxAnnotation();
            return annotation == null ? processed : (T)processed.WithAdditionalAnnotations(annotation);
        }
    }
}
