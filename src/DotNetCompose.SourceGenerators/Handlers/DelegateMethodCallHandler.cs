using DotNetCompose.SourceGenerators;
using DotNetCompose.SourceGenerators.Extensions;
using DotNetCompose.SourceGenerators.Helpers;
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

            ExpressionSyntax changedArg = ArgumentResolver.BuildChangedArg(delegateParams, invocation.ArgumentList.Arguments, methodCtx);

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
