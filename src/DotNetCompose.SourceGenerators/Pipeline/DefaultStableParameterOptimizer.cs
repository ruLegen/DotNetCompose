using DotNetCompose.SourceGenerators.Extensions;
using DotNetCompose.SourceGenerators.Helpers;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Linq;
using static DotNetCompose.SourceGenerators.Consts;

namespace DotNetCompose.SourceGenerators.Pipeline
{
    internal sealed class DefaultStableParameterOptimizer : IStableParameterOptimizer
    {
        public BlockSyntax OptimizeStableParameters(BlockSyntax body, TransformationContext context)
        {
            var methodCtx = context.MethodCtx;
            var options = context.Options;
            var session = context.Session;

            var normalParams = methodCtx.Parameters
                .Select((p, i) => (Param: p, Index: i))
                .Where(x => !x.Param.IsComposable)
                .ToList();

            bool anyNormalParams = normalParams.Any();
            bool allStable = anyNormalParams
                && normalParams.All(x => x.Param.Type != null && x.Param.Type.IsStableType());

            if (!allStable || !anyNormalParams)
                return body;

            using var prologueStmts = ListPool<StatementSyntax>.Get();
            var stateVarNames = new List<string>();
            string ctxVar = options.ContextVarName;
            string changedVar = options.ChangedVarName;

            foreach (var (param, index) in normalParams)
            {
                string stateVar = $"__{param.Name}_state";
                stateVarNames.Add(stateVar);

                if (param.DefaultProviderType != null)
                {
                    var conditionalExpr = SyntaxFactory.ConditionalExpression(
                        SyntaxFactory.BinaryExpression(
                            SyntaxKind.EqualsExpression,
                            SyntaxFactory.ElementAccessExpression(
                                SyntaxFactory.IdentifierName(Consts.Rewriter.DefaultParamName))
                            .WithArgumentList(SyntaxFactory.BracketedArgumentList(
                                SyntaxFactory.SingletonSeparatedList(
                                    SyntaxFactory.Argument(
                                        SyntaxFactoryHelpers.CreateIntLiteral(param.DefaultIndex))))),
                            SyntaxFactory.MemberAccessExpression(
                                SyntaxKind.SimpleMemberAccessExpression,
                                SyntaxFactory.ParseTypeName(Consts.ComposableArgumentsDefaultState.FullName),
                                SyntaxFactory.IdentifierName("ShouldUseDefault"))),
                        SyntaxFactory.MemberAccessExpression(
                            SyntaxKind.SimpleMemberAccessExpression,
                            SyntaxFactory.ParseTypeName(Consts.ComposableArgumentsState.FullName),
                            SyntaxFactory.IdentifierName(Consts.ComposableArgumentsState.StaticField)),
                        SyntaxFactory.ElementAccessExpression(
                            SyntaxFactory.IdentifierName(changedVar))
                        .WithArgumentList(SyntaxFactory.BracketedArgumentList(
                            SyntaxFactory.SingletonSeparatedList(
                                SyntaxFactory.Argument(
                                    SyntaxFactoryHelpers.CreateIntLiteral(index))))));

                    prologueStmts.Add(SyntaxFactory.LocalDeclarationStatement(
                        SyntaxFactory.VariableDeclaration(
                            SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.ByteKeyword)))
                        .WithVariables(
                            SyntaxFactory.SingletonSeparatedList(
                                SyntaxFactory.VariableDeclarator(
                                    SyntaxFactory.Identifier(stateVar))
                                .WithInitializer(SyntaxFactory.EqualsValueClause(conditionalExpr)))))
                        .WithTrailingNewLine());
                }
                else
                {
                    prologueStmts.Add(SyntaxFactory.LocalDeclarationStatement(
                        SyntaxFactory.VariableDeclaration(
                            SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.ByteKeyword)))
                        .WithVariables(
                            SyntaxFactory.SingletonSeparatedList(
                                SyntaxFactory.VariableDeclarator(
                                    SyntaxFactory.Identifier(stateVar))
                                .WithInitializer(SyntaxFactory.EqualsValueClause(
                                    SyntaxFactory.ElementAccessExpression(
                                        SyntaxFactory.IdentifierName(changedVar))
                                    .WithArgumentList(SyntaxFactory.BracketedArgumentList(
                                        SyntaxFactory.SingletonSeparatedList(
                                            SyntaxFactory.Argument(
                                                SyntaxFactoryHelpers.CreateIntLiteral(index))))))))))
                        .WithTrailingNewLine());
                }

                if (param.Type != null && param.Type.IsStableType())
                {
                    prologueStmts.Add(SyntaxFactory.IfStatement(
                        SyntaxFactory.BinaryExpression(
                            SyntaxKind.EqualsExpression,
                            SyntaxFactory.IdentifierName(stateVar),
                            SyntaxFactory.MemberAccessExpression(
                                SyntaxKind.SimpleMemberAccessExpression,
                                SyntaxFactory.ParseTypeName(Consts.ComposableArgumentsState.FullName),
                                SyntaxFactory.IdentifierName(Consts.ComposableArgumentsState.UncertainField))),
                        SyntaxFactory.Block(
                            SyntaxFactory.SingletonList<StatementSyntax>(
                                SyntaxFactory.ExpressionStatement(
                                    SyntaxFactory.AssignmentExpression(
                                        SyntaxKind.SimpleAssignmentExpression,
                                        SyntaxFactory.IdentifierName(stateVar),
                                        SyntaxFactory.ConditionalExpression(
                                            SyntaxFactory.InvocationExpression(
                                                SyntaxFactory.MemberAccessExpression(
                                                    SyntaxKind.SimpleMemberAccessExpression,
                                                    SyntaxFactory.IdentifierName(ctxVar),
                                                    SyntaxFactory.IdentifierName(Consts.ComposeContext.ChangedMethod)))
                                            .WithArgumentList(SyntaxFactory.ArgumentList(
                                                SyntaxFactory.SingletonSeparatedList(
                                                    SyntaxFactory.Argument(SyntaxFactory.IdentifierName(param.Name))))),
                                            SyntaxFactory.MemberAccessExpression(
                                                SyntaxKind.SimpleMemberAccessExpression,
                                                SyntaxFactory.ParseTypeName(Consts.ComposableArgumentsState.FullName),
                                                SyntaxFactory.IdentifierName(Consts.ComposableArgumentsState.DifferentField)),
                                            SyntaxFactory.MemberAccessExpression(
                                                SyntaxKind.SimpleMemberAccessExpression,
                                                SyntaxFactory.ParseTypeName(Consts.ComposableArgumentsState.FullName),
                                                SyntaxFactory.IdentifierName(Consts.ComposableArgumentsState.SameField)))))
                                .WithTrailingNewLine()))));
                }
            }

            ExpressionSyntax? condition = null;
            foreach (var stateVar in stateVarNames)
            {
                var eqToSame = SyntaxFactory.BinaryExpression(
                    SyntaxKind.EqualsExpression,
                    SyntaxFactory.IdentifierName(stateVar),
                    SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        SyntaxFactory.ParseTypeName(Consts.ComposableArgumentsState.FullName),
                        SyntaxFactory.IdentifierName(Consts.ComposableArgumentsState.SameField)));

                var eqToStatic = SyntaxFactory.BinaryExpression(
                    SyntaxKind.EqualsExpression,
                    SyntaxFactory.IdentifierName(stateVar),
                    SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        SyntaxFactory.ParseTypeName(Consts.ComposableArgumentsState.FullName),
                        SyntaxFactory.IdentifierName(Consts.ComposableArgumentsState.StaticField)));

                var eq = SyntaxFactory.ParenthesizedExpression(
                    SyntaxFactory.BinaryExpression(
                        SyntaxKind.LogicalOrExpression,
                        eqToSame,
                        eqToStatic));

                condition = condition == null
                    ? eq
                    : SyntaxFactory.BinaryExpression(SyntaxKind.LogicalAndExpression, condition, eq);
            }

            condition = SyntaxFactory.BinaryExpression(
                SyntaxKind.LogicalAndExpression,
                condition,
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    SyntaxFactory.IdentifierName(ctxVar),
                    SyntaxFactory.IdentifierName(Consts.ComposeContext.SkippingProperty)));

            var skipStatement = SyntaxFactory.IfStatement(
                condition,
                SyntaxFactory.Block(
                    SyntaxFactory.SingletonList<StatementSyntax>(
                        SyntaxFactory.ExpressionStatement(
                            SyntaxFactory.InvocationExpression(
                                SyntaxFactory.MemberAccessExpression(
                                    SyntaxKind.SimpleMemberAccessExpression,
                                    SyntaxFactory.IdentifierName(ctxVar),
                                    SyntaxFactory.IdentifierName(Consts.ComposeContext.SkipToGroupEndMethod))))
                            .WithTrailingNewLine())),
                SyntaxFactory.ElseClause(
                    SyntaxFactory.Block(body.Statements)));

            using var allStmts = ListPool<StatementSyntax>.Get();
            allStmts.AddRange(prologueStmts);
            allStmts.Add(skipStatement);

            return SyntaxFactory.Block(allStmts).WithTrailingNewLine();
        }
    }
}
