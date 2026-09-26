using DotNetCompose.SourceGenerators.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Linq;

namespace DotNetCompose.SourceGenerators.Pipeline
{
    internal sealed class DefaultBodyWrappingStrategy : IBodyWrappingStrategy
    {
        public BlockSyntax WrapMethodBody(BlockSyntax body, TransformationContext context)
        {
            if (!context.MethodCtx.GeneratesRestartGroup)
                return body;
            RewriterOptions options = context.Options;
            RewriterSession session = context.Session;
            MethodGenerationContext methodContext = context.MethodCtx;
            string contextVariable = options.ContextVarName;
            string changedVariable = options.ChangedVarName;
            string defaultStateVariable = options.DefaultParamName;

            HashSet<string> usedNames = new HashSet<string>(
                body.DescendantTokens()
                    .Where(token => token.IsKind(SyntaxKind.IdentifierToken))
                    .Select(token => token.ValueText));
            foreach (MethodDeclarationSyntaxExtensions.MethodParameterInfo parameter in methodContext.Parameters)
                usedNames.Add(parameter.Name);
            usedNames.Add(contextVariable);
            usedNames.Add(changedVariable);
            usedNames.Add(defaultStateVariable);

            string scopeUpdaterName = AllocateName(usedNames, "__dncScopeUpdater");
            string restartContextName = AllocateName(usedNames, "__dncRestartContext");

            List<StatementSyntax> tryStatements = new List<StatementSyntax>();
            tryStatements.Add(SyntaxFactoryHelpers.CreateMethodCallOnIdentifierWithArgs(
                contextVariable,
                Consts.ComposeContext.StartRestartableGroupMethod,
                SyntaxFactoryHelpers.CreateIntLiteral(session.InitialGroupId)));
            tryStatements.AddRange(body.Statements);

            LocalDeclarationStatementSyntax scopeUpdaterDeclaration = SyntaxFactory.LocalDeclarationStatement(
                SyntaxFactory.VariableDeclaration(
                    SyntaxFactory.NullableType(
                        SyntaxFactory.ParseTypeName(Consts.ComposeUpdateScope.FullName)))
                .WithVariables(
                    SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.VariableDeclarator(
                            SyntaxFactory.Identifier(scopeUpdaterName))
                        .WithInitializer(
                            SyntaxFactory.EqualsValueClause(
                                SyntaxFactoryHelpers.CreateMethodCallSyntaxWithArgs(
                                    contextVariable,
                                    Consts.ComposeContext.EndRestartableGroupMethod,
                                    SyntaxFactoryHelpers.CreateIntLiteral(session.InitialGroupId)))))))
                .WithTrailingNewLine();

            List<string> restartChangedNames = new List<string>(methodContext.Parameters.Length);
            List<StatementSyntax> restartStatements = new List<StatementSyntax>();
            for (int index = 0; index < methodContext.Parameters.Length; index++)
            {
                string captureName = AllocateName(usedNames, $"__dncRestartChanged{index}");
                restartChangedNames.Add(captureName);
                restartStatements.Add(SyntaxFactory.ParseStatement(
                    $"byte {captureName} = {Consts.ComposableArgumentsState.FullName}.{Consts.ComposableArgumentsState.NormalizeForRestartMethod}({changedVariable}[{index}]);"));
            }

            int defaultParameterCount = methodContext.Parameters.Count(parameter => parameter.DefaultProviderType != null);
            List<string> restartDefaultNames = new List<string>(defaultParameterCount);
            for (int index = 0; index < defaultParameterCount; index++)
            {
                string captureName = AllocateName(usedNames, $"__dncRestartDefault{index}");
                restartDefaultNames.Add(captureName);
                restartStatements.Add(SyntaxFactory.ParseStatement(
                    $"byte {captureName} = {defaultStateVariable}[{index}];"));
            }

            ExpressionSyntax methodNameExpression;
            if (methodContext.TypeParameterNames.Any())
            {
                methodNameExpression = SyntaxFactory.GenericName(
                    SyntaxFactory.Identifier(methodContext.MethodName),
                    SyntaxFactory.TypeArgumentList(
                        SyntaxFactory.SeparatedList<TypeSyntax>(
                            methodContext.TypeParameterNames.Select(name =>
                                SyntaxFactory.IdentifierName(name) as TypeSyntax))));
            }
            else
            {
                methodNameExpression = SyntaxFactory.IdentifierName(methodContext.MethodName);
            }

            List<ArgumentSyntax> restartArguments = new List<ArgumentSyntax>();
            foreach (MethodDeclarationSyntaxExtensions.MethodParameterInfo parameter in methodContext.Parameters)
                restartArguments.Add(SyntaxFactory.Argument(SyntaxFactory.IdentifierName(parameter.Name)));
            restartArguments.Add(SyntaxFactory.Argument(SyntaxFactory.IdentifierName(restartContextName)));
            restartArguments.Add(SyntaxFactory.Argument(BuildRestartChangedExpression(restartChangedNames)));
            restartArguments.Add(SyntaxFactory.Argument(BuildRestartDefaultExpression(restartDefaultNames)));

            InvocationExpressionSyntax selfCall = SyntaxFactory.InvocationExpression(methodNameExpression)
                .WithArgumentList(
                    SyntaxFactory.ArgumentList(
                        SyntaxFactory.SeparatedList(restartArguments)));

            SimpleLambdaExpressionSyntax restartLambda = SyntaxFactory.SimpleLambdaExpression(
                SyntaxFactory.Parameter(SyntaxFactory.Identifier(restartContextName)),
                SyntaxFactory.Block(
                    SyntaxFactory.SingletonList<StatementSyntax>(
                        SyntaxFactory.ExpressionStatement(selfCall)
                            .WithTrailingNewLine())));

            ExpressionStatementSyntax updateScopeCall = SyntaxFactory.ExpressionStatement(
                SyntaxFactory.InvocationExpression(
                    SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        SyntaxFactory.IdentifierName(scopeUpdaterName),
                        SyntaxFactory.IdentifierName(Consts.ComposeUpdateScope.UpdateScopeMethod)))
                .WithArgumentList(
                    SyntaxFactory.ArgumentList(
                        SyntaxFactory.SingletonSeparatedList(
                            SyntaxFactory.Argument(restartLambda)))))
                .WithTrailingNewLine();
            restartStatements.Add(updateScopeCall);

            IfStatementSyntax updateScopeStatement = SyntaxFactory.IfStatement(
                SyntaxFactory.BinaryExpression(
                    SyntaxKind.NotEqualsExpression,
                    SyntaxFactory.IdentifierName(scopeUpdaterName),
                    SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression)),
                SyntaxFactory.Block(restartStatements))
                .WithTrailingNewLine();

            StatementSyntax tryFinallyStatement = SyntaxFactory.TryStatement(
                    SyntaxFactory.Block(tryStatements),
                    default,
                    SyntaxFactory.FinallyClause(
                        SyntaxFactory.Block(
                            scopeUpdaterDeclaration,
                            updateScopeStatement)))
                .WithTrailingNewLine();

            return SyntaxFactory.Block(SyntaxFactory.SingletonList(tryFinallyStatement));
        }

        private static ExpressionSyntax BuildRestartChangedExpression(IReadOnlyList<string> captureNames)
        {
            if (captureNames.Count == 0)
            {
                return SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    SyntaxFactory.ParseTypeName(Consts.ComposableArgumentsState.FullName),
                    SyntaxFactory.IdentifierName(Consts.ComposableArgumentsState.ForceField));
            }

            string values = string.Join(", ", captureNames);
            return SyntaxFactory.ParseExpression(
                $"{Consts.ComposableArgumentsState.FullName}.{Consts.ComposableArgumentsState.ForcedMethod}(stackalloc byte[] {{ {values} }})");
        }

        private static ExpressionSyntax BuildRestartDefaultExpression(IReadOnlyList<string> captureNames)
        {
            if (captureNames.Count == 0)
                return SyntaxFactory.LiteralExpression(SyntaxKind.DefaultLiteralExpression);

            string values = string.Join(", ", captureNames);
            return SyntaxFactory.ParseExpression(
                $"new {Consts.ComposableArgumentsDefaultState.FullName}(stackalloc byte[] {{ {values} }})");
        }

        private static string AllocateName(HashSet<string> usedNames, string baseName)
        {
            string candidate = baseName;
            int suffix = 0;
            while (!usedNames.Add(candidate))
            {
                suffix++;
                candidate = $"{baseName}_{suffix}";
            }
            return candidate;
        }
    }
}
