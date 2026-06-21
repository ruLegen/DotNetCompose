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
                var invArg = args[j];
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
            MethodGenerationContext methodCtx)
        {
            if (methodCtx.HasUnstableParam)
                return SyntaxFactory.LiteralExpression(SyntaxKind.DefaultLiteralExpression);

            using ListPoolObject<ExpressionSyntax> stateExprs = ListPool<ExpressionSyntax>.Get();
            bool allSame = true;

            for (int i = 0; i < calleeParams.Length; i++)
            {
                var calleeParam = calleeParams[i];

                int argIdx = FindArgumentIndex(args, i, calleeParam.Name);

                if (argIdx == -1)
                {
                    stateExprs.Add(SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        SyntaxFactory.ParseTypeName(ComposableArgumentsState.FullName),
                        SyntaxFactory.IdentifierName(ComposableArgumentsState.UncertainField)));
                    allSame = false;
                    continue;
                }

                if (calleeParam.IsComposable)
                {
                    stateExprs.Add(SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        SyntaxFactory.ParseTypeName(ComposableArgumentsState.FullName),
                        SyntaxFactory.IdentifierName(ComposableArgumentsState.DifferentField)));
                    allSame = false;
                    continue;
                }

                var expr = args[argIdx].Expression;

                if (expr is LiteralExpressionSyntax)
                {
                    stateExprs.Add(SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        SyntaxFactory.ParseTypeName(ComposableArgumentsState.FullName),
                        SyntaxFactory.IdentifierName(ComposableArgumentsState.StaticField)));
                    continue;
                }

                if (expr is IdentifierNameSyntax idName)
                {
                    var callerParams = methodCtx.Parameters;
                    bool found = false;
                    for (int cp = 0; cp < callerParams.Length; cp++)
                    {
                        if (callerParams[cp].Name == idName.Identifier.Text)
                        {
                            stateExprs.Add(SyntaxFactory.IdentifierName($"__{idName.Identifier.Text}_state"));
                            allSame = false;
                            found = true;
                            break;
                        }
                    }
                    if (found) continue;
                }

                stateExprs.Add(SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    SyntaxFactory.ParseTypeName(ComposableArgumentsState.FullName),
                    SyntaxFactory.IdentifierName(ComposableArgumentsState.UncertainField)));
                allSame = false;
            }

            if (allSame)
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
