using DotNetCompose.SourceGenerators.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using static DotNetCompose.SourceGenerators.ComposableMethodGeneratorContext;
using static DotNetCompose.SourceGenerators.Consts;
using static DotNetCompose.SourceGenerators.Extensions.MethodDeclarationSyntaxExtensions;

#nullable enable
namespace DotNetCompose.SourceGenerators.Rewriters
{
    internal class ComposeMethodRewriter : ComposableSyntaxRewriterBase
    {
        internal ComposeMethodRewriter(ComposableMethodGeneratorContext ctx, SemanticModel semanticModel)
            : base(ctx, semanticModel)
        {
        }

        protected override ExpressionSyntax? ProcessInvokeMethodExpression(ExpressionSyntax expression, IMethodSymbol methodSymbol)
        {
            if (methodSymbol.MethodKind == MethodKind.Ordinary)
            {
                if (!methodSymbol.IsComposableFunction())
                    return null;
                if (expression is not InvocationExpressionSyntax invocationExpression)
                    throw new NotSupportedException();

                return VisitComposableMethodCall(invocationExpression);
            }
            else if (methodSymbol.MethodKind == MethodKind.DelegateInvoke)
            {
                DelegateMethodCallInfo? delegateMethodCallInfo = GetDelegateMethodCallInfo(expression, methodSymbol);
                if (delegateMethodCallInfo == null)
                    return null;

                bool isComposableArgumentCall = _ctx.MethodParameters.FirstOrDefault(p => p.Name == delegateMethodCallInfo.RecieverObjectName)?.IsComposable ?? false;
                if (!isComposableArgumentCall)
                    return null;

                return VisitComposableArgumentCall(expression, delegateMethodCallInfo);
            }
            else
            {
                return null;
            }
        }

        protected override ExpressionSyntax VisitComposableMethodCall(InvocationExpressionSyntax invocationExpression)
        {
            IMethodSymbol? methodSymbol = _semanticModel.GetSymbolInfo(invocationExpression).Symbol as IMethodSymbol;
            if (methodSymbol == null)
                return invocationExpression;

            ImmutableArray<MethodParameterInfo> parameterInfos = methodSymbol.GetParametersInfos(_semanticModel);

            using ListPoolObject<(ArgumentSyntax Argument, bool IsComposable)> arguments = ListPool<(ArgumentSyntax, bool)>.Get();
            arguments.AddRange(invocationExpression.ArgumentList.Arguments.Select((arg, index) =>
            {
                bool isComposable = false;
                if (arg.NameColon != null)
                {
                    MethodParameterInfo? argInfo = parameterInfos.FirstOrDefault(a => a.Name == arg.NameColon.Name.Identifier.ValueText);
                    isComposable = argInfo?.IsComposable ?? false;
                }
                else
                {
                    isComposable = parameterInfos[index].IsComposable;
                }
                return (arg, isComposable);
            }));

            IEnumerable<ArgumentSyntax> processedArgs = arguments.Select(a =>
                {
                    ArgumentSyntax arg = a.Argument;
                    bool isComposable = a.IsComposable;
                    if (!isComposable)
                        return arg;

                    if (arg.Expression is IdentifierNameSyntax identifierName)
                    {
                        IMethodSymbol? argumentMethod = _semanticModel.GetSymbolInfo(identifierName).Symbol as IMethodSymbol;
                        if (argumentMethod != null)
                            throw new NotSupportedException("Composable method referencing is not supported");
                    }

                    ImmutableArray<ParameterSyntax> lambdaParameters;
                    bool isCaptureAnything = false;
                    CSharpSyntaxNode newBody = default;
                    if (arg.Expression is SimpleLambdaExpressionSyntax simpleLambdaExpression)
                    {
                        lambdaParameters = ImmutableArray.Create<ParameterSyntax>(simpleLambdaExpression.Parameter);
                        DataFlowAnalysis analizeInfo = _semanticModel.AnalyzeDataFlow(simpleLambdaExpression.Body);
                        isCaptureAnything = analizeInfo.CapturedInside.Length > 0;
                        newBody = base.Visit(simpleLambdaExpression.Body) as CSharpSyntaxNode;
                    }
                    else if (arg.Expression is ParenthesizedLambdaExpressionSyntax parenthesizedLambdaExpression)
                    {
                        lambdaParameters = parenthesizedLambdaExpression.ParameterList.Parameters.ToImmutableArray();
                        DataFlowAnalysis analizeInfo = _semanticModel.AnalyzeDataFlow(parenthesizedLambdaExpression.Body);
                        isCaptureAnything = analizeInfo.CapturedInside.Length > 0;
                        newBody = base.Visit(parenthesizedLambdaExpression.Body) as CSharpSyntaxNode;
                    }
                    else
                        throw new NotSupportedException();

                    ImmutableArray<(string Type, string Name)> argTypes = lambdaParameters.Select(item =>
                    {
                        IParameterSymbol s = _semanticModel.GetDeclaredSymbol(item);
                        return (Type: s.Type.GetFullMetadataName(), Name: s.Name);
                    }).ToImmutableArray();

                    ImmutableArray<(string Type, string Name)> newArgs = argTypes.AddRange(new (string Type, string Name)[] {
                            (Consts.ComposeContext.FullName, _ctx.ContextVarName),
                            (Consts.ComposableArgumentsState.FullName, _ctx.ChangedVarName),
                        });

                    var newParamList = SyntaxFactory.ParameterList(
                        SyntaxFactory.SeparatedList(newArgs.Select(item =>
                            SyntaxFactory.Parameter(
                                default,
                                default,
                                 SyntaxFactory.ParseTypeName(item.Type).WithTrailingSpace(),
                                 SyntaxFactory.Identifier(item.Name),
                                 null)
                            ))
                    );

                    if (isCaptureAnything)
                    {
                        TypeSyntax variableType = default;
                        if (argTypes.Length == 0)
                        {
                            variableType = SyntaxFactory.IdentifierName(Consts.ComposableAction.FullName);
                        }
                        else
                        {
                            variableType = SyntaxFactory.GenericName(
                                            SyntaxFactory.Identifier(Consts.ComposableAction.FullName),
                                            SyntaxFactory.TypeArgumentList(SyntaxFactory.SeparatedList(argTypes.Select(t => SyntaxFactory.ParseTypeName(t.Type)))));
                        }
                        variableType = variableType.WithTrailingSpace();

                        var wrappedLambdaExpression = SyntaxFactoryHelpers.CreateMethodCallSyntaxWithArgs("ComposeHelpers", "GetLambda",
                            SyntaxFactory.IdentifierName(_ctx.ContextVarName),
                            SyntaxFactoryHelpers.CreateIntLiteral(_ctx.GetNextLambdaKey()),
                            SyntaxFactory.ParenthesizedLambdaExpression(
                                SyntaxFactory.ParameterList(),
                                SyntaxFactory.Block(
                                    SyntaxFactory.LocalDeclarationStatement(
                                        SyntaxFactory.VariableDeclaration(variableType).AddVariables(
                                            SyntaxFactory.VariableDeclarator("a").WithInitializer(
                                                SyntaxFactory.EqualsValueClause(SyntaxFactory.ParenthesizedLambdaExpression(newParamList, newBody))).WithLeadingSpace())
                                    ),
                                    SyntaxFactory.ReturnStatement(SyntaxFactory.IdentifierName("a").WithLeadingSpace())
                                                .WithLeadingNewLine()
                               )
                            ));
                        MemberAccessExpressionSyntax newLambdaExpression = default;
                        if (argTypes.Length == 0)
                        {
                            newLambdaExpression = SyntaxFactory.MemberAccessExpression(
                                                           SyntaxKind.SimpleMemberAccessExpression,
                                                           wrappedLambdaExpression,
                                                           SyntaxFactory.IdentifierName("Invoke"));
                        }
                        else
                        {
                            newLambdaExpression = SyntaxFactory.MemberAccessExpression(
                                                           SyntaxKind.SimpleMemberAccessExpression,
                                                           wrappedLambdaExpression,
                                                           SyntaxFactory.GenericName(
                                                                SyntaxFactory.Identifier("Invoke"),
                                                                SyntaxFactory.TypeArgumentList(
                                                                    SyntaxFactory.SeparatedList(argTypes.Select(t => SyntaxFactory.ParseTypeName(t.Type)))
                                                                )
                                                           ));
                        }
                        return arg.WithExpression(newLambdaExpression);
                    }
                    else
                    {
                        string name = _ctx.GetNextLambdaName();

                        SyntaxTokenList lamdaModifiers = default(SyntaxTokenList).AddRange(new SyntaxToken[]
                        {
                            SyntaxFactory.Token(SyntaxKind.PublicKeyword),
                            SyntaxFactory.Token(SyntaxKind.StaticKeyword),
                        });
                        BlockSyntax newBodyBlockSyntax = newBody switch
                        {
                            BlockSyntax block => block,
                            ArrowExpressionClauseSyntax arrowExpression => SyntaxFactory.Block(SyntaxFactory.ExpressionStatement(arrowExpression.Expression)),
                            _ => throw new NotSupportedException(),
                        };
                        MethodDeclarationSyntax lambdaMethodDeclaration = SyntaxFactory.MethodDeclaration(default,
                             lamdaModifiers,
                             SyntaxFactory.ParseTypeName("void").WithTrailingSpace(),
                             default,
                             SyntaxFactory.Identifier(name),
                             default,
                             newParamList.WithTrailingNewLine(),
                             default,
                             newBodyBlockSyntax.WithTrailingNewLine(),
                             default(SyntaxToken));

                        _ctx.AddStoredLambda(new StoredLambda(name, newArgs, lambdaMethodDeclaration));

                        return arg.WithExpression(SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                                                            SyntaxFactory.IdentifierName(_ctx.StoredLambdaIdentifierName),
                                                            SyntaxFactory.IdentifierName(name)));
                    }
                });

            ExpressionSyntax changedArg;
            if (_ctx.HasUnstableParam)
            {
                changedArg = SyntaxFactory.LiteralExpression(SyntaxKind.DefaultLiteralExpression);
            }
            else
            {
                using ListPoolObject<ExpressionSyntax> stateExprs = ListPool<ExpressionSyntax>.Get();
                bool allSame = true;

                for (int i = 0; i < parameterInfos.Length; i++)
                {
                    var calleeParam = parameterInfos[i];

                    int argIdx = -1;
                    for (int j = 0; j < invocationExpression.ArgumentList.Arguments.Count; j++)
                    {
                        var invArg = invocationExpression.ArgumentList.Arguments[j];
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

                    var argEntry = arguments[argIdx];

                    if (argEntry.IsComposable)
                    {
                        stateExprs.Add(SyntaxFactory.MemberAccessExpression(
                            SyntaxKind.SimpleMemberAccessExpression,
                            SyntaxFactory.ParseTypeName(Consts.ComposableArgumentsState.FullName),
                            SyntaxFactory.IdentifierName(Consts.ComposableArgumentsState.DifferentField)));
                        allSame = false;
                        continue;
                    }

                    var expr = argEntry.Argument.Expression;

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
                        var callerParams = _ctx.MethodParameters;
                        bool found = false;
                        for (int cp = 0; cp < callerParams.Length; cp++)
                        {
                            if (callerParams[cp].Name == idName.Identifier.Text)
                            {
                                stateExprs.Add(SyntaxFactory.CastExpression(
                                    SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.ByteKeyword)),
                                    SyntaxFactory.ElementAccessExpression(
                                        SyntaxFactory.IdentifierName(_ctx.ChangedVarName))
                                        .WithArgumentList(SyntaxFactory.BracketedArgumentList(
                                            SyntaxFactory.SingletonSeparatedList(
                                                SyntaxFactory.Argument(SyntaxFactoryHelpers.CreateIntLiteral(cp)))))));
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
                {
                    changedArg = SyntaxFactory.LiteralExpression(SyntaxKind.DefaultLiteralExpression);
                }
                else
                {
                    changedArg = SyntaxFactory.ObjectCreationExpression(
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
            }

            ArgumentListSyntax newArgs = SyntaxFactory.ArgumentList(
                SyntaxFactory.SeparatedList<ArgumentSyntax>(processedArgs).AddRange(
                    new ArgumentSyntax[]{
                        SyntaxFactory.Argument(SyntaxFactory.IdentifierName(_ctx.ContextVarName)),
                        SyntaxFactory.Argument(changedArg),
                    })
            );
            _ctx.ComposableProcessed();

            invocationExpression = ReplaceWithFullQualifiedName(invocationExpression);
            MemberAccessExpressionSyntax? lastmemberAccess = invocationExpression.DescendantNodes().OfType<MemberAccessExpressionSyntax>().FirstOrDefault();
            if (lastmemberAccess != null)
            {
                string lastAccessedMemberName = lastmemberAccess.Name.ToFullString();
                string newAccessMemberName = $"{_ctx.BuildersClassName}.{lastAccessedMemberName}";
                invocationExpression = (InvocationExpressionSyntax)ReplaceLastMemberAccess(invocationExpression, lastAccessedMemberName, newAccessMemberName);
                return invocationExpression.WithArgumentList(newArgs);
            }
            throw new NotSupportedException();
        }

        protected override ExpressionSyntax VisitComposableArgumentCall(ExpressionSyntax expression, DelegateMethodCallInfo delegateMethodCallInfo)
        {
            ExpressionSyntax result = null;
            if (delegateMethodCallInfo.IsSimpleMemberAccessCall)
            {
                var invocationSyntax = expression as InvocationExpressionSyntax;
                ArgumentListSyntax newArguments = invocationSyntax.ArgumentList.AddArguments(
                   new ArgumentSyntax[]
                   {
                        SyntaxFactory.Argument(SyntaxFactory.IdentifierName(_ctx.ContextVarName)),
                        SyntaxFactory.Argument(SyntaxFactory.IdentifierName(_ctx.ChangedVarName)),
                   }
                );
                result = invocationSyntax.WithArgumentList(newArguments);
            }
            else if (delegateMethodCallInfo.IsDirectCall)
            {
                InvocationExpressionSyntax invocation = expression as InvocationExpressionSyntax;
                result = invocation.WithArgumentList(invocation.ArgumentList.AddArguments(
                    new ArgumentSyntax[]{
                        SyntaxFactory.Argument(SyntaxFactory.IdentifierName(_ctx.ContextVarName)),
                        SyntaxFactory.Argument(SyntaxFactory.IdentifierName(_ctx.ChangedVarName)),
                    }
                ));
            }
            else if (delegateMethodCallInfo.IsNullSafeCall)
            {
                ConditionalAccessExpressionSyntax conditionalAccessExpression = expression as ConditionalAccessExpressionSyntax;
                InvocationExpressionSyntax invocation = conditionalAccessExpression.WhenNotNull as InvocationExpressionSyntax;
                if (invocation == null)
                    new NotSupportedException();

                ArgumentListSyntax newArguments = invocation.ArgumentList.AddArguments(
                      new ArgumentSyntax[]{
                        SyntaxFactory.Argument(SyntaxFactory.IdentifierName(_ctx.ContextVarName)),
                        SyntaxFactory.Argument(SyntaxFactory.IdentifierName(_ctx.ChangedVarName)),
                    });

                result = conditionalAccessExpression.WithWhenNotNull(
                    invocation.WithArgumentList(newArguments));
            }
            if (result != null)
            {
                _ctx.ComposableProcessed();
                return result;
            }else
                return expression;
        }

      

        internal static SyntaxNode? Rewrite(ComposableMethodGeneratorContext ctx, SemanticModel semanticModel, MethodDeclarationSyntax method)
        {
            ComposeMethodRewriter rewriter = new ComposeMethodRewriter(ctx, semanticModel);
            return rewriter.Visit(method);
        }

        internal static ParameterListSyntax ReplaceAllComposableParameters(MethodDeclarationSyntax method, SemanticModel semanticModel, bool addAttributeToComposableParameters)
        {
            var tempCtx = new ComposableMethodGeneratorContext(Consts.Rewriter.ContextParamName, Consts.Rewriter.ChangedParamName);
            var rewriter = new ComposeMethodRewriter(tempCtx, semanticModel);
            return rewriter.ReplaceAllComposableParameters(method, addAttributeToComposableParameters);
        }

        internal static ParameterListSyntax AppendComposableContextrelatedParameters(ParameterListSyntax paramList, SemanticModel semanticModel, string contextParamName, string changedParamName)
        {
            var tempCtx = new ComposableMethodGeneratorContext(contextParamName, changedParamName);
            var rewriter = new ComposeMethodRewriter(tempCtx, semanticModel);
            return rewriter.AppendComposableContextrelatedParameters(paramList, contextParamName, changedParamName);
        }
    }
}
