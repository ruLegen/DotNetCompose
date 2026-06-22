using DotNetCompose.SourceGenerators.Extensions;
using DotNetCompose.SourceGenerators.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;
using static DotNetCompose.SourceGenerators.Consts;

namespace DotNetCompose.SourceGenerators.Pipeline
{
    internal sealed class DefaultValueSubstitutor : IDefaultValueSubstitutor
    {
        public BlockSyntax SubstituteDefaults(BlockSyntax body, TransformationContext context)
        {
            var methodCtx = context.MethodCtx;
            if (!methodCtx.HasDefaultParams)
                return body;

            using var substStmts = ListPool<StatementSyntax>.Get();
            for (int i = 0; i < methodCtx.Parameters.Length; i++)
            {
                var p = methodCtx.Parameters[i];
                if (p.DefaultProviderType == null) continue;

                string providerTypeName = p.DefaultProviderType.ToDisplayString(
                    SymbolDisplayFormat.FullyQualifiedFormat
                        .WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Included));

                var condition = SyntaxFactory.BinaryExpression(
                    SyntaxKind.EqualsExpression,
                    SyntaxFactory.ElementAccessExpression(
                        SyntaxFactory.IdentifierName(Consts.Rewriter.DefaultParamName))
                    .WithArgumentList(SyntaxFactory.BracketedArgumentList(
                        SyntaxFactory.SingletonSeparatedList(
                            SyntaxFactory.Argument(
                                SyntaxFactoryHelpers.CreateIntLiteral(p.DefaultIndex))))),
                    SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        SyntaxFactory.ParseTypeName(Consts.ComposableArgumentsDefaultState.FullName),
                        SyntaxFactory.IdentifierName("ShouldUseDefault")));

                var assignment = SyntaxFactory.AssignmentExpression(
                    SyntaxKind.SimpleAssignmentExpression,
                    SyntaxFactory.IdentifierName(p.Name),
                    SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        SyntaxFactory.ParseTypeName(providerTypeName),
                        SyntaxFactory.IdentifierName("Value")));

                substStmts.Add(SyntaxFactory.IfStatement(
                    condition,
                    SyntaxFactory.Block(
                        SyntaxFactory.SingletonList<StatementSyntax>(
                            SyntaxFactory.ExpressionStatement(assignment).WithTrailingNewLine()))));
            }

            if (substStmts.Count > 0)
            {
                var origStmts = body.Statements.ToArray();
                var newStmts = new StatementSyntax[substStmts.Count + origStmts.Length];
                substStmts.CopyTo(newStmts, 0);
                origStmts.CopyTo(newStmts, substStmts.Count);
                body = body.WithStatements(SyntaxFactory.List(newStmts));
            }

            return body;
        }
    }
}
