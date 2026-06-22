using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotNetCompose.SourceGenerators.Pipeline
{
    internal interface IControlFlowStrategy
    {
        IfStatementSyntax RewriteIf(IfStatementSyntax ifStatement, TransformationContext context);
        ForStatementSyntax RewriteFor(ForStatementSyntax forStatement, TransformationContext context);
        ForEachStatementSyntax RewriteForEach(ForEachStatementSyntax forEachStatement, TransformationContext context);
    }
}
