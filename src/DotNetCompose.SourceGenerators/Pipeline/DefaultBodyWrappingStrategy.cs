using DotNetCompose.SourceGenerators.Extensions;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;

namespace DotNetCompose.SourceGenerators.Pipeline
{
    internal sealed class DefaultBodyWrappingStrategy : IBodyWrappingStrategy
    {
        public BlockSyntax WrapMethodBody(BlockSyntax body, TransformationContext context)
        {
            var options = context.Options;
            var session = context.Session;
            var methodCtx = context.MethodCtx;
            string ctxVar = options.ContextVarName;
            string defaultParamVar = options.DefaultParamName;

            using var tryStatements = ListPool<StatementSyntax>.Get();

            tryStatements.Add(SyntaxFactoryHelpers.CreateSafeMethodCallOnVariableWithArgs(
                ctxVar,
                Consts.ComposeContext.StartRestartableGroupMethod,
                SyntaxFactoryHelpers.CreateIntLiteral(session.InitialGroupId)));

            tryStatements.AddRange(body.Statements);

            // IComposeUpdateScope? scopeUpdater = __ctx?.EndRestartableGroup(groupId);
            LocalDeclarationStatementSyntax scopeUpdaterDecl = SyntaxFactory.LocalDeclarationStatement(
                SyntaxFactory.VariableDeclaration(
                    SyntaxFactory.NullableType(
                        SyntaxFactory.ParseTypeName(Consts.ComposeUpdateScope.FullName))
                ).WithVariables(
                    SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.VariableDeclarator(
                            SyntaxFactory.Identifier("scopeUpdater"))
                        .WithInitializer(
                            SyntaxFactory.EqualsValueClause(
                                SyntaxFactory.ConditionalAccessExpression(
                                    SyntaxFactory.IdentifierName(ctxVar),
                                    SyntaxFactory.InvocationExpression(
                                        SyntaxFactory.MemberBindingExpression(
                                            SyntaxFactory.IdentifierName(Consts.ComposeContext.EndRestartableGroupMethod))
                                    ).WithArgumentList(
                                        SyntaxFactory.ArgumentList(
                                            SyntaxFactory.SingletonSeparatedList(
                                                SyntaxFactory.Argument(
                                                    SyntaxFactoryHelpers.CreateIntLiteral(session.InitialGroupId))
                                            )
                                        )
                                    )
                                )
                            )
                        )
                    )
                )
            ).WithTrailingNewLine();

            // byte[] __defaultStateValues = __defaultParamState.CloneValues();
            LocalDeclarationStatementSyntax defaultParamsDecl = SyntaxFactory.LocalDeclarationStatement(
                SyntaxFactory.VariableDeclaration(
                    SyntaxFactory.ArrayType(
                        SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.ByteKeyword))
                    ).WithRankSpecifiers(
                        SyntaxFactory.SingletonList(
                            SyntaxFactory.ArrayRankSpecifier(
                                SyntaxFactory.SingletonSeparatedList<ExpressionSyntax>(
                                    SyntaxFactory.OmittedArraySizeExpression())
                            )
                        )
                    )
                ).WithVariables(
                    SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.VariableDeclarator(
                            SyntaxFactory.Identifier("__defaultStateValues"))
                        .WithInitializer(
                            SyntaxFactory.EqualsValueClause(
                                SyntaxFactory.InvocationExpression(
                                    SyntaxFactory.MemberAccessExpression(
                                        SyntaxKind.SimpleMemberAccessExpression,
                                        SyntaxFactory.IdentifierName(defaultParamVar),
                                        SyntaxFactory.IdentifierName("CloneValues"))
                                )
                            )
                        )
                    )
                )
            ).WithTrailingNewLine();

            // Build method name (with generics if needed)
            ExpressionSyntax methodNameExpr;
            if (methodCtx.TypeParameterNames.Any())
            {
                methodNameExpr = SyntaxFactory.GenericName(
                    SyntaxFactory.Identifier(methodCtx.MethodName),
                    SyntaxFactory.TypeArgumentList(
                        SyntaxFactory.SeparatedList<TypeSyntax>(
                            methodCtx.TypeParameterNames.Select(name =>
                                SyntaxFactory.IdentifierName(name) as TypeSyntax
                            )
                        )
                    )
                );
            }
            else
            {
                methodNameExpr = SyntaxFactory.IdentifierName(methodCtx.MethodName);
            }

            // Build arguments for the self-call
            using var args = ListPool<ArgumentSyntax>.Get();
            foreach (var param in methodCtx.Parameters)
            {
                args.Add(SyntaxFactory.Argument(SyntaxFactory.IdentifierName(param.Name)));
            }

            args.Add(SyntaxFactory.Argument(SyntaxFactory.IdentifierName("context")));

            args.Add(SyntaxFactory.Argument(
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    SyntaxFactory.ParseTypeName(Consts.ComposableArgumentsState.FullName),
                    SyntaxFactory.IdentifierName(Consts.ComposableArgumentsState.ForceField)
                )
            ));

            args.Add(SyntaxFactory.Argument(
                SyntaxFactory.ObjectCreationExpression(
                    SyntaxFactory.ParseTypeName(Consts.ComposableArgumentsDefaultState.FullName)
                ).WithArgumentList(
                    SyntaxFactory.ArgumentList(
                        SyntaxFactory.SingletonSeparatedList(
                            SyntaxFactory.Argument(
                                SyntaxFactory.IdentifierName("__defaultStateValues"))
                        )
                    )
                )
            ));

            InvocationExpressionSyntax selfCall = SyntaxFactory.InvocationExpression(methodNameExpr)
                .WithArgumentList(
                    SyntaxFactory.ArgumentList(
                        SyntaxFactory.SeparatedList(args.ToArray())
                    )
                );

            // Build lambda: context => { MethodName<T,K>(...); }
            SimpleLambdaExpressionSyntax lambda = SyntaxFactory.SimpleLambdaExpression(
                SyntaxFactory.Parameter(SyntaxFactory.Identifier("context")),
                SyntaxFactory.Block(
                    SyntaxFactory.SingletonList<StatementSyntax>(
                        SyntaxFactory.ExpressionStatement(selfCall)
                            .WithTrailingNewLine()
                    )
                )
            );

            // scopeUpdater.UpdateScope(lambda)
            ExpressionStatementSyntax updateScopeCall = SyntaxFactory.ExpressionStatement(
                SyntaxFactory.InvocationExpression(
                    SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        SyntaxFactory.IdentifierName("scopeUpdater"),
                        SyntaxFactory.IdentifierName(Consts.ComposeUpdateScope.UpdateScopeMethod)
                    )
                ).WithArgumentList(
                    SyntaxFactory.ArgumentList(
                        SyntaxFactory.SingletonSeparatedList(
                            SyntaxFactory.Argument(lambda)
                        )
                    )
                )
            ).WithTrailingNewLine();

            // if (scopeUpdater != null) { ... }
            IfStatementSyntax ifStmt = SyntaxFactory.IfStatement(
                SyntaxFactory.BinaryExpression(
                    SyntaxKind.NotEqualsExpression,
                    SyntaxFactory.IdentifierName("scopeUpdater"),
                    SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression)
                ),
                SyntaxFactory.Block(
                    defaultParamsDecl,
                    updateScopeCall
                )
            ).WithTrailingNewLine();

            StatementSyntax tryFinallyNode = SyntaxFactory.TryStatement(
                    SyntaxFactory.Block(tryStatements),
                    default,
                    SyntaxFactory.FinallyClause(
                        SyntaxFactory.Block(
                            scopeUpdaterDecl,
                            ifStmt
                        )
                    ))
                    .WithTrailingNewLine();

            return SyntaxFactory.Block(SyntaxFactory.SingletonList(tryFinallyNode));
        }
    }
}
