using DotNetCompose.SourceGenerators;
using DotNetCompose.SourceGenerators.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Immutable;
using System.Linq;
using static DotNetCompose.SourceGenerators.Consts;
using static DotNetCompose.SourceGenerators.Extensions.MethodDeclarationSyntaxExtensions;

namespace DotNetCompose.SourceGenerators.Handlers
{
    internal record DelegateMethodCallInfo(string RecieverObjectName, bool IsSimpleMemberAccessCall, bool IsDirectCall, bool IsNullSafeCall);

    internal class DelegateMethodCallHandler : IMethodCallHandler
    {
        public InterceptionResult Handle(
            ExpressionSyntax expression,
            IMethodSymbol methodSymbol,
            MethodCallHandlerContext context,
            out SyntaxNode? replacement)
        {
            replacement = null;

            if (methodSymbol.MethodKind != MethodKind.DelegateInvoke)
                return InterceptionResult.Continue;

            DelegateMethodCallInfo? delegateMethodCallInfo = GetDelegateMethodCallInfo(expression, methodSymbol);
            if (delegateMethodCallInfo == null)
                return InterceptionResult.Continue;

            bool isComposableArgumentCall = context.MethodCtx.Parameters
                .FirstOrDefault(p => p.Name == delegateMethodCallInfo.RecieverObjectName)?.IsComposable ?? false;
            if (!isComposableArgumentCall)
                return InterceptionResult.Continue;

            replacement = ProcessDelegateCall(expression, delegateMethodCallInfo, context);
            return InterceptionResult.Handled;
        }

        private ExpressionSyntax ProcessDelegateCall(
            ExpressionSyntax expression,
            DelegateMethodCallInfo delegateMethodCallInfo,
            MethodCallHandlerContext context)
        {
            var options = context.Options;
            var methodCtx = context.MethodCtx;
            var session = context.Session;
            var semanticModel = context.SemanticModel;

            InvocationExpressionSyntax? invocation = null;
            if (delegateMethodCallInfo.IsSimpleMemberAccessCall || delegateMethodCallInfo.IsDirectCall)
            {
                invocation = expression as InvocationExpressionSyntax;
            }
            else if (delegateMethodCallInfo.IsNullSafeCall)
            {
                var conditionalAccess = expression as ConditionalAccessExpressionSyntax;
                invocation = conditionalAccess?.WhenNotNull as InvocationExpressionSyntax;
            }

            if (invocation == null)
                return expression;

            IMethodSymbol? delegateMethod = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
            ImmutableArray<MethodParameterInfo> delegateParams = ImmutableArray<MethodParameterInfo>.Empty;
            if (delegateMethod != null)
                delegateParams = delegateMethod.GetParametersInfos(semanticModel);

            ExpressionSyntax changedArg = BuildChangedArg(delegateParams, invocation.ArgumentList.Arguments, methodCtx);

            ExpressionSyntax result = null;
            if (delegateMethodCallInfo.IsSimpleMemberAccessCall)
            {
                var invocationSyntax = expression as InvocationExpressionSyntax;
                ArgumentListSyntax newArguments = invocationSyntax.ArgumentList.AddArguments(
                   new ArgumentSyntax[]
                   {
                         SyntaxFactory.Argument(SyntaxFactory.IdentifierName(options.ContextVarName)),
                         SyntaxFactory.Argument(changedArg),
                         SyntaxFactory.Argument(SyntaxFactory.IdentifierName(Rewriter.DefaultParamName)),
                   }
                );
                result = invocationSyntax.WithArgumentList(newArguments);
            }
            else if (delegateMethodCallInfo.IsDirectCall)
            {
                InvocationExpressionSyntax inv = expression as InvocationExpressionSyntax;
                result = inv.WithArgumentList(inv.ArgumentList.AddArguments(
                    new ArgumentSyntax[]{
                        SyntaxFactory.Argument(SyntaxFactory.IdentifierName(options.ContextVarName)),
                        SyntaxFactory.Argument(changedArg),
                        SyntaxFactory.Argument(SyntaxFactory.IdentifierName(Rewriter.DefaultParamName)),
                    }
                ));
            }
            else if (delegateMethodCallInfo.IsNullSafeCall)
            {
                ConditionalAccessExpressionSyntax conditionalAccessExpression = expression as ConditionalAccessExpressionSyntax;
                InvocationExpressionSyntax inv = conditionalAccessExpression.WhenNotNull as InvocationExpressionSyntax;
                if (inv == null)
                    new NotSupportedException();

                ArgumentListSyntax newArguments = inv.ArgumentList.AddArguments(
                      new ArgumentSyntax[]{
                        SyntaxFactory.Argument(SyntaxFactory.IdentifierName(options.ContextVarName)),
                        SyntaxFactory.Argument(changedArg),
                        SyntaxFactory.Argument(SyntaxFactory.IdentifierName(Rewriter.DefaultParamName)),
                    });

                result = conditionalAccessExpression.WithWhenNotNull(
                    inv.WithArgumentList(newArguments));
            }
            if (result != null)
            {
                session.MarkComposableProcessed();
                return result;
            }
            else
                return expression;
        }

        private static ExpressionSyntax BuildChangedArg(
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

                int argIdx = -1;
                for (int j = 0; j < args.Count; j++)
                {
                    var invArg = args[j];
                    if (invArg.NameColon != null)
                    {
                        if (invArg.NameColon.Name.Identifier.ValueText == calleeParam.Name)
                        {
                            argIdx = j;
                            break;
                        }
                    }
                    else if (j == i)
                    {
                        argIdx = j;
                        break;
                    }
                }

                if (argIdx == -1)
                {
                    stateExprs.Add(SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        SyntaxFactory.ParseTypeName(Consts.ComposableArgumentsState.FullName),
                        SyntaxFactory.IdentifierName(Consts.ComposableArgumentsState.UncertainField)));
                    allSame = false;
                    continue;
                }

                if (calleeParam.IsComposable)
                {
                    stateExprs.Add(SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        SyntaxFactory.ParseTypeName(Consts.ComposableArgumentsState.FullName),
                        SyntaxFactory.IdentifierName(Consts.ComposableArgumentsState.DifferentField)));
                    allSame = false;
                    continue;
                }

                var expr = args[argIdx].Expression;

                if (expr is LiteralExpressionSyntax)
                {
                    stateExprs.Add(SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        SyntaxFactory.ParseTypeName(Consts.ComposableArgumentsState.FullName),
                        SyntaxFactory.IdentifierName(Consts.ComposableArgumentsState.StaticField)));
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
                    SyntaxFactory.ParseTypeName(Consts.ComposableArgumentsState.FullName),
                    SyntaxFactory.IdentifierName(Consts.ComposableArgumentsState.UncertainField)));
                allSame = false;
            }

            if (allSame)
                return SyntaxFactory.LiteralExpression(SyntaxKind.DefaultLiteralExpression);

            return SyntaxFactory.ObjectCreationExpression(
                SyntaxFactory.ParseTypeName(Consts.ComposableArgumentsState.FullName))
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

        private static DelegateMethodCallInfo? GetDelegateMethodCallInfo(ExpressionSyntax expression, IMethodSymbol methodSymbol)
        {
            bool isSimpleMemberAccess = false;
            bool isDirectCall = false;
            bool isNullSafeCall = false;
            string recieverObjectName = string.Empty;

            if (expression is InvocationExpressionSyntax invocationExpression)
            {
                switch (invocationExpression.Expression)
                {
                    case IdentifierNameSyntax identifierNameSyntax:
                        recieverObjectName = identifierNameSyntax.Identifier.Text;
                        isDirectCall = true;
                        break;
                    case MemberAccessExpressionSyntax memberAccessExpressionSyntax:
                        recieverObjectName = (memberAccessExpressionSyntax.Expression as IdentifierNameSyntax)?.Identifier.Text;
                        isSimpleMemberAccess = true;
                        break;
                    default:
                        return null;
                }
            }
            else if (expression is ConditionalAccessExpressionSyntax conditionalAccessExpression)
            {
                switch (conditionalAccessExpression.Expression)
                {
                    case IdentifierNameSyntax identifierNameSyntax:
                        recieverObjectName = identifierNameSyntax.Identifier.Text;
                        isNullSafeCall = true;
                        break;
                    default:
                        return null;
                }
            }
            else
                return null;

            return new DelegateMethodCallInfo(recieverObjectName, isSimpleMemberAccess, isDirectCall, isNullSafeCall);
        }
    }
}
