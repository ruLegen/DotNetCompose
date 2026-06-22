using DotNetCompose.SourceGenerators.Extensions;
using DotNetCompose.SourceGenerators.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Linq;
using static DotNetCompose.SourceGenerators.Consts;

namespace DotNetCompose.SourceGenerators.Pipeline
{
    internal sealed class DefaultParameterChangedTransformer : IParameterChangedTransformer
    {
        /// <summary>
        /// Adds check for parameter changes
        /// </summary>
        /// <param name="body"></param>
        /// <param name="context"></param>
        /// <returns></returns>
        public BlockSyntax TransformParameters(BlockSyntax body, TransformationContext context)
        {
            MethodGenerationContext methodCtx = context.MethodCtx;
            RewriterOptions options = context.Options;
            RewriterSession session = context.Session;

            var normalParams = methodCtx.Parameters
                .Select((p, i) => (Param: p, Index: i))
                .Where(x => !x.Param.IsComposable)
                .ToList();

            bool anyNormalParams = normalParams.Any();
            bool allStable = anyNormalParams
                && normalParams.All(x => x.Param.Type != null && x.Param.Type.IsStableType());

            if (!allStable || !anyNormalParams)
                return body;

            using ListPoolObject<StatementSyntax> prologueStmts = ListPool<StatementSyntax>.Get();
            using ListPoolObject<string> stateVarNames = ListPool<string>.Get();
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
            foreach (string stateVar in stateVarNames)
            {
                BinaryExpressionSyntax eqToSame = SyntaxFactory.BinaryExpression(
                    SyntaxKind.EqualsExpression,
                    SyntaxFactory.IdentifierName(stateVar),
                    SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        SyntaxFactory.ParseTypeName(Consts.ComposableArgumentsState.FullName),
                        SyntaxFactory.IdentifierName(Consts.ComposableArgumentsState.SameField)));

                BinaryExpressionSyntax eqToStatic = SyntaxFactory.BinaryExpression(
                    SyntaxKind.EqualsExpression,
                    SyntaxFactory.IdentifierName(stateVar),
                    SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        SyntaxFactory.ParseTypeName(Consts.ComposableArgumentsState.FullName),
                        SyntaxFactory.IdentifierName(Consts.ComposableArgumentsState.StaticField)));

                ParenthesizedExpressionSyntax eq = SyntaxFactory.ParenthesizedExpression(
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


            IEnumerable<StatementSyntax> allStmts = Enumerable.Concat(prologueStmts, new StatementSyntax[] { skipStatement });
            return SyntaxFactory.Block(allStmts).WithTrailingNewLine();
        }
    }
}
