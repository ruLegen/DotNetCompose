using DotNetCompose.SourceGenerators.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using static DotNetCompose.SourceGenerators.Consts;
using static DotNetCompose.SourceGenerators.Extensions.MethodDeclarationSyntaxExtensions;

namespace DotNetCompose.SourceGenerators.Helpers
{
    internal static class ArgumentResolver
    {
        public static int FindArgumentIndex(
            SeparatedSyntaxList<ArgumentSyntax> args,
            int paramIndex,
            string paramName)
        {
            for (int j = 0; j < args.Count; j++)
            {
                ArgumentSyntax invArg = args[j];
                if (invArg.NameColon != null)
                {
                    if (invArg.NameColon.Name.Identifier.ValueText == paramName)
                        return j;
                }
                else if (j == paramIndex)
                {
                    return j;
                }
            }
            return -1;
        }

        public static ArgumentSyntax? TryGetArgument(
            SeparatedSyntaxList<ArgumentSyntax> args,
            int paramIndex,
            string paramName)
        {
            int idx = FindArgumentIndex(args, paramIndex, paramName);
            return idx >= 0 ? args[idx] : null;
        }

        public static ExpressionSyntax BuildChangedArg(
            ImmutableArray<MethodParameterInfo> calleeParams,
            SeparatedSyntaxList<ArgumentSyntax> args,
            MethodGenerationContext methodCtx,
            SemanticModel semanticModel)
        {
            using ListPoolObject<ExpressionSyntax> stateExprs = ListPool<ExpressionSyntax>.Get();
            bool hasKnownState = false;

            for (int i = 0; i < calleeParams.Length; i++)
            {
                MethodParameterInfo calleeParam = calleeParams[i];

                int argIdx = FindArgumentIndex(args, i, calleeParam.Name);

                if (argIdx == -1)
                {
                    stateExprs.Add(SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        SyntaxFactory.ParseTypeName(ComposableArgumentsState.FullName),
                        SyntaxFactory.IdentifierName(ComposableArgumentsState.UncertainField)));
                    continue;
                }

                ExpressionSyntax expr = args[argIdx].Expression;

                if (expr is IdentifierNameSyntax idName)
                {
                    ImmutableArray<MethodParameterInfo> callerParams = methodCtx.Parameters;
                    bool found = false;
                    for (int cp = 0; cp < callerParams.Length; cp++)
                    {
                        if (callerParams[cp].Name == idName.Identifier.Text)
                        {
                            stateExprs.Add(SyntaxFactory.IdentifierName($"__{idName.Identifier.Text}_state"));
                            hasKnownState = true;
                            found = true;
                            break;
                        }
                    }
                    if (found)
                        continue;
                }

                if (calleeParam.IsComposable)
                {
                    bool isStaticLambda = expr switch
                    {
                        SimpleLambdaExpressionSyntax simple =>
                            semanticModel.AnalyzeDataFlow(simple.Body).CapturedInside.Length == 0,
                        ParenthesizedLambdaExpressionSyntax parenthesized =>
                            semanticModel.AnalyzeDataFlow(parenthesized.Body).CapturedInside.Length == 0,
                        _ => false,
                    };
                    stateExprs.Add(SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        SyntaxFactory.ParseTypeName(ComposableArgumentsState.FullName),
                        SyntaxFactory.IdentifierName(isStaticLambda
                            ? ComposableArgumentsState.StaticField
                            : ComposableArgumentsState.UncertainField)));
                    if (isStaticLambda)
                        hasKnownState = true;
                    continue;
                }

                if (expr is LiteralExpressionSyntax)
                {
                    stateExprs.Add(SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        SyntaxFactory.ParseTypeName(ComposableArgumentsState.FullName),
                        SyntaxFactory.IdentifierName(ComposableArgumentsState.StaticField)));
                    hasKnownState = true;
                    continue;
                }

                stateExprs.Add(SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    SyntaxFactory.ParseTypeName(ComposableArgumentsState.FullName),
                    SyntaxFactory.IdentifierName(ComposableArgumentsState.UncertainField)));
            }

            if (!hasKnownState)
                return SyntaxFactory.LiteralExpression(SyntaxKind.DefaultLiteralExpression);

            return SyntaxFactory.ObjectCreationExpression(
                SyntaxFactory.ParseTypeName(ComposableArgumentsState.FullName))
                .WithArgumentList(SyntaxFactory.ArgumentList(
                    SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.Argument(
                            SyntaxFactory.StackAllocArrayCreationExpression(
                                SyntaxFactory.ArrayType(
                                    SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.ByteKeyword)),
                                    SyntaxFactory.SingletonList(
                                        SyntaxFactory.ArrayRankSpecifier())),
                                SyntaxFactory.InitializerExpression(
                                    SyntaxKind.ArrayInitializerExpression,
                                    SyntaxFactory.SeparatedList(stateExprs)))))));
        }
    }
}
