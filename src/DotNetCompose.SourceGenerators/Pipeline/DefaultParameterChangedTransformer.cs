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
            if (context.IsReadOnly)
                return body;
            MethodGenerationContext methodCtx = context.MethodCtx;
            RewriterOptions options = context.Options;
            bool canSkip = methodCtx.CanSkip;
            List<(MethodDeclarationSyntaxExtensions.MethodParameterInfo Param, int Index)> trackedParams = methodCtx.Parameters
                .Select((p, i) => (Param: p, Index: i))
                .ToList();

            if (!trackedParams.Any())
                return body;

            using ListPoolObject<StatementSyntax> prologueStmts = ListPool<StatementSyntax>.Get();
            using ListPoolObject<string> stateVarNames = ListPool<string>.Get();
            string ctxVar = options.ContextVarName;
            string changedVar = options.ChangedVarName;

            foreach ((MethodDeclarationSyntaxExtensions.MethodParameterInfo param, int index) in trackedParams)
            {
                string stateVar = $"__{param.Name}_state";
                stateVarNames.Add(stateVar);

                if (param.DefaultProviderType != null)
                {
                    ConditionalExpressionSyntax conditionalExpr = SyntaxFactory.ConditionalExpression(
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

                if (canSkip)
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

            if (!canSkip)
            {
                IEnumerable<StatementSyntax> propagatedStatements = Enumerable.Concat(
                    prologueStmts,
                    body.Statements);
                return SyntaxFactory.Block(propagatedStatements).WithTrailingNewLine();
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

            ExpressionSyntax notForced = SyntaxFactory.PrefixUnaryExpression(
                SyntaxKind.LogicalNotExpression,
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    SyntaxFactory.IdentifierName(changedVar),
                    SyntaxFactory.IdentifierName(Consts.ComposableArgumentsState.IsForcedProperty)));

            condition = SyntaxFactory.BinaryExpression(
                SyntaxKind.LogicalAndExpression,
                notForced,
                SyntaxFactory.BinaryExpression(
                    SyntaxKind.LogicalAndExpression,
                    condition,
                    SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        SyntaxFactory.IdentifierName(ctxVar),
                        SyntaxFactory.IdentifierName(Consts.ComposeContext.SkippingProperty))));

            IfStatementSyntax skipStatement = SyntaxFactory.IfStatement(
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
