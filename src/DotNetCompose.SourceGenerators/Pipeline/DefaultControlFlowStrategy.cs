using DotNetCompose.SourceGenerators.Diagnostics;
using DotNetCompose.SourceGenerators.Extensions;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Linq;

namespace DotNetCompose.SourceGenerators.Pipeline
{
    internal sealed class DefaultControlFlowStrategy : IControlFlowStrategy
    {
        public IfStatementSyntax RewriteIf(IfStatementSyntax node, TransformationContext context)
        {
            var session = context.Session;
            var options = context.Options;

            // Extract already-visited statements from the if-block
            IReadOnlyList<StatementSyntax> ifStatements;
            if (node.Statement is BlockSyntax block)
                ifStatements = block.Statements.ToList();
            else if (node.Statement is ExpressionStatementSyntax expr)
                ifStatements = new[] { expr };
            else
            {
                session.Report(DiagnosticInfo.Create(
                    DiagnosticDescriptors.DNC002_IfWithoutBlock,
                    node.GetLocation()));
                return node;
            }

            // Extract already-visited statements from the else-block
            IReadOnlyList<StatementSyntax>? elseStatements = null;
            bool isElseIfBlock = false;
            if (node.Else?.Statement is BlockSyntax elseBlock)
                elseStatements = elseBlock.Statements.ToList();
            else if (node.Else?.Statement is ExpressionStatementSyntax elseExpr)
                elseStatements = new[] { elseExpr };
            else if (node.Else?.Statement is IfStatementSyntax)
            {
                elseStatements = new[] { node.Else.Statement };
                isElseIfBlock = true;
            }

            if (!session.WasInConditional)
                return node;

            int ifGroupId = session.NextGroupId();
            ElseClauseSyntax? newElseClauseSyntax = node.Else;

            if (elseStatements != null && elseStatements.Any())
            {
                int elseGroupId = session.NextGroupId();
                ExpressionStatementSyntax elseGroupStartStatement = SyntaxFactoryHelpers.CreateSafeMethodCallOnVariableWithArgs(
                    options.ContextVarName,
                    Consts.ComposeContext.StartReplaceableGroupMethod,
                    SyntaxFactoryHelpers.CreateIntLiteral(elseGroupId))
                    .WithTrailingNewLine();
                ExpressionStatementSyntax elseGroupEndStatement = SyntaxFactoryHelpers.CreateSafeMethodCallOnVariableWithArgs(
                    options.ContextVarName,
                    Consts.ComposeContext.EndReplaceableGroupMethod,
                    SyntaxFactoryHelpers.CreateIntLiteral(elseGroupId));

                if (isElseIfBlock)
                {
                    StatementSyntax stmt = elseStatements.Count == 1
                        ? elseStatements[0]
                        : SyntaxFactory.Block(elseStatements);

                    newElseClauseSyntax = node.Else
                        .WithStatement(stmt)
                        .WithTrailingNewLine();
                }
                else
                {
                    newElseClauseSyntax = node.Else.WithStatement(SyntaxFactory.Block(
                        WrapWithGroupMethods(elseGroupStartStatement, elseStatements, elseGroupEndStatement)))
                        .WithTrailingNewLine();
                }
            }

            IfStatementSyntax newIfStatement = node.WithElse(newElseClauseSyntax);

            if (ifStatements.Any())
            {
                ExpressionStatementSyntax ifGroupStartStatement = SyntaxFactoryHelpers.CreateSafeMethodCallOnVariableWithArgs(
                    options.ContextVarName,
                    Consts.ComposeContext.StartReplaceableGroupMethod,
                    SyntaxFactoryHelpers.CreateIntLiteral(ifGroupId))
                    .WithTrailingNewLine();

                ExpressionStatementSyntax ifGroupEndStatement = SyntaxFactoryHelpers.CreateSafeMethodCallOnVariableWithArgs(
                    options.ContextVarName,
                    Consts.ComposeContext.EndReplaceableGroupMethod,
                    SyntaxFactoryHelpers.CreateIntLiteral(ifGroupId));

                newIfStatement = node.WithStatement(SyntaxFactory.Block(
                    WrapWithGroupMethods(ifGroupStartStatement, ifStatements, ifGroupEndStatement)))
                    .WithElse(newElseClauseSyntax)
                    .WithTrailingNewLine();
            }

            return newIfStatement;
        }

        public ForStatementSyntax RewriteFor(ForStatementSyntax forStatement, TransformationContext context)
        {
            var session = context.Session;

            if (forStatement.Statement is BlockSyntax block)
            {
                return forStatement.WithStatement(block).WithTrailingNewLine();
            }

            session.Report(DiagnosticInfo.Create(
                DiagnosticDescriptors.DNC003_ForWithoutBlock,
                forStatement.GetLocation()));
            return forStatement;
        }

        public ForEachStatementSyntax RewriteForEach(ForEachStatementSyntax forEachStatement, TransformationContext context)
        {
            var session = context.Session;

            if (forEachStatement.Statement is BlockSyntax block)
            {
                return forEachStatement.WithStatement(block).WithTrailingNewLine();
            }

            session.Report(DiagnosticInfo.Create(
                DiagnosticDescriptors.DNC004_ForeachWithoutBlock,
                forEachStatement.GetLocation()));
            return forEachStatement;
        }

        private static IEnumerable<StatementSyntax> WrapWithGroupMethods(
            ExpressionStatementSyntax groupStart,
            IReadOnlyList<StatementSyntax> statements,
            ExpressionStatementSyntax groupEnd)
        {
            yield return groupStart;
            foreach (var statement in statements)
            {
                yield return statement;
            }
            yield return groupEnd;
        }
    }
}
