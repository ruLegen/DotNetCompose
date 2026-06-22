using DotNetCompose.SourceGenerators.Extensions;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotNetCompose.SourceGenerators.Pipeline
{
    internal sealed class DefaultBodyWrappingStrategy : IBodyWrappingStrategy
    {
        public BlockSyntax WrapMethodBody(BlockSyntax body, TransformationContext context)
        {
            var options = context.Options;
            var session = context.Session;
            string ctxVar = options.ContextVarName;

            using var tryStatements = ListPool<StatementSyntax>.Get();

            tryStatements.Add(SyntaxFactoryHelpers.CreateSafeMethodCallOnVariableWithArgs(
                ctxVar,
                Consts.ComposeContext.StartRestartableGroupMethod,
                SyntaxFactoryHelpers.CreateIntLiteral(session.InitialGroupId)));

            tryStatements.AddRange(body.Statements);

            ExpressionStatementSyntax endGroupStatement = SyntaxFactoryHelpers.CreateSafeMethodCallOnVariableWithArgs(
                ctxVar,
                Consts.ComposeContext.EndRestartableGroupMethod,
                SyntaxFactoryHelpers.CreateIntLiteral(session.InitialGroupId));

            StatementSyntax tryFinallyNode = SyntaxFactory.TryStatement(
                    SyntaxFactory.Block(tryStatements),
                    default,
                    SyntaxFactory.FinallyClause(SyntaxFactory.Block(SyntaxFactory.SingletonList<StatementSyntax>(endGroupStatement))))
                    .WithTrailingNewLine();

            return SyntaxFactory.Block(SyntaxFactory.SingletonList(tryFinallyNode));
        }
    }
}
