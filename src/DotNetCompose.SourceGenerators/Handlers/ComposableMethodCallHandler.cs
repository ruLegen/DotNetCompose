using DotNetCompose.SourceGenerators.Diagnostics;
using DotNetCompose.SourceGenerators.Extensions;
using DotNetCompose.SourceGenerators.Helpers;
using DotNetCompose.SourceGenerators.Rewriters;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using static DotNetCompose.SourceGenerators.Consts;
using static DotNetCompose.SourceGenerators.Extensions.MethodDeclarationSyntaxExtensions;

namespace DotNetCompose.SourceGenerators.Handlers
{
    internal class ComposableMethodCallHandler : IMethodCallHandler
    {
        public bool TryHandle(
            ExpressionSyntax expression,
            IMethodSymbol methodSymbol,
            MethodCallHandlerContext context,
            out SyntaxNode? replacement)
        {
            replacement = null;

            if (methodSymbol.MethodKind != MethodKind.Ordinary)
                return false;
            if (!methodSymbol.IsComposableFunction())
                return false;
            if (expression is not InvocationExpressionSyntax invocationExpression)
                return false;

            replacement = ProcessComposableCall(invocationExpression, methodSymbol, context);
            return true;
        }

        private InvocationExpressionSyntax ProcessComposableCall(
            InvocationExpressionSyntax invocationExpression,
            IMethodSymbol methodSymbol,
            MethodCallHandlerContext context)
        {
            var options = context.Options;
            var methodCtx = context.MethodCtx;
            var session = context.Session;
            var semanticModel = context.SemanticModel;

            ImmutableArray<MethodParameterInfo> parameterInfos = methodSymbol.GetParametersInfos(semanticModel);

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
                    IMethodSymbol? argumentMethod = semanticModel.GetSymbolInfo(identifierName).Symbol as IMethodSymbol;
                    if (argumentMethod != null)
                    {
                        context.Diagnostics.Report(DiagnosticInfo.Create(
                            DiagnosticDescriptors.DNC005_DirectComposableReference,
                            identifierName.GetLocation(),
                            argumentMethod.Name));
                        return arg;
                    }
                }

                ImmutableArray<ParameterSyntax> lambdaParameters;
                bool isCaptureAnything = false;
                CSharpSyntaxNode newBody = default;
                Func<SyntaxNode, SyntaxNode?> visit = context.VisitNode ?? (n => n);
                if (arg.Expression is SimpleLambdaExpressionSyntax simpleLambdaExpression)
                {
                    lambdaParameters = ImmutableArray.Create<ParameterSyntax>(simpleLambdaExpression.Parameter);
                    DataFlowAnalysis analizeInfo = semanticModel.AnalyzeDataFlow(simpleLambdaExpression.Body);
                    isCaptureAnything = analizeInfo.CapturedInside.Length > 0;
                    newBody = (CSharpSyntaxNode)visit(simpleLambdaExpression.Body);
                }
                else if (arg.Expression is ParenthesizedLambdaExpressionSyntax parenthesizedLambdaExpression)
                {
                    lambdaParameters = parenthesizedLambdaExpression.ParameterList.Parameters.ToImmutableArray();
                    DataFlowAnalysis analizeInfo = semanticModel.AnalyzeDataFlow(parenthesizedLambdaExpression.Body);
                    isCaptureAnything = analizeInfo.CapturedInside.Length > 0;
                    newBody = (CSharpSyntaxNode)visit(parenthesizedLambdaExpression.Body);
                }
                else
                {
                    context.Diagnostics.Report(DiagnosticInfo.Create(
                        DiagnosticDescriptors.DNC006_UnrecognizedLambda,
                        arg.Expression.GetLocation()));
                    return arg;
                }

                ImmutableArray<(string Type, string Name)> argTypes = lambdaParameters.Select(item =>
                {
                    IParameterSymbol s = semanticModel.GetDeclaredSymbol(item);
                    return (Type: s.Type.GetFullMetadataName(), Name: s.Name);
                }).ToImmutableArray();

                ImmutableArray<(string Type, string Name)> newArgs = argTypes.AddRange(new (string Type, string Name)[] {
                    (Consts.ComposeContext.FullName, options.ContextVarName),
                    (Consts.ComposableArgumentsState.FullName, options.ChangedVarName),
                    (Consts.ComposableArgumentsDefaultState.FullName, Consts.Rewriter.DefaultParamName),
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
                        SyntaxFactory.IdentifierName(options.ContextVarName),
                        SyntaxFactoryHelpers.CreateIntLiteral(session.NextLambdaKey()),
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
                    string name = session.NextLambdaName();

                    SyntaxTokenList lamdaModifiers = default(SyntaxTokenList).AddRange(new SyntaxToken[]
                    {
                        SyntaxFactory.Token(SyntaxKind.PublicKeyword),
                        SyntaxFactory.Token(SyntaxKind.StaticKeyword),
                    });
                    bool bodyConversionFailed = newBody is not BlockSyntax and not ArrowExpressionClauseSyntax;
                    BlockSyntax newBodyBlockSyntax = newBody switch
                    {
                        BlockSyntax block => block,
                        ArrowExpressionClauseSyntax arrowExpression => SyntaxFactory.Block(SyntaxFactory.ExpressionStatement(arrowExpression.Expression)),
                        _ => SyntaxFactory.Block(),
                    };
                    if (bodyConversionFailed)
                    {
                        context.Diagnostics.Report(DiagnosticInfo.Create(
                            DiagnosticDescriptors.DNC007_LambdaBodyConversion,
                            arg.Expression.GetLocation()));
                    }
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

                    session.AddStoredLambda(new RewriterSession.StoredLambda(name, newArgs, lambdaMethodDeclaration));

                    return arg.WithExpression(SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                                                        SyntaxFactory.IdentifierName(options.StoredLambdaClassName),
                                                        SyntaxFactory.IdentifierName(name)));
                }
            });

            ExpressionSyntax changedArg = ArgumentResolver.BuildChangedArg(parameterInfos, invocationExpression.ArgumentList.Arguments, methodCtx);

            int defaultCount = parameterInfos.Count(p => p.DefaultProviderType != null);
            bool anyShouldUseDefault = false;
            byte[] defaultStateBytes = new byte[defaultCount];
            for (int i = 0; i < methodSymbol.Parameters.Length; i++)
            {
                var paramInfo = parameterInfos[i];
                if (paramInfo.DefaultProviderType == null) continue;

                int argIdx = ArgumentResolver.FindArgumentIndex(invocationExpression.ArgumentList.Arguments, i, paramInfo.Name);

                if (argIdx >= 0)
                {
                    var argExpr = invocationExpression.ArgumentList.Arguments[argIdx].Expression;
                    if (argExpr.IsKind(SyntaxKind.DefaultLiteralExpression))
                    {
                        defaultStateBytes[paramInfo.DefaultIndex] = 1;
                        anyShouldUseDefault = true;
                        continue;
                    }
                }
                else
                {
                    defaultStateBytes[paramInfo.DefaultIndex] = 1;
                    anyShouldUseDefault = true;
                    continue;
                }
                defaultStateBytes[paramInfo.DefaultIndex] = 0;
            }

            ArgumentSyntax defaultStateArg;
            if (!anyShouldUseDefault)
            {
                defaultStateArg = SyntaxFactory.Argument(
                    SyntaxFactory.LiteralExpression(SyntaxKind.DefaultLiteralExpression));
            }
            else
            {
                var byteExprs = defaultStateBytes.Select(b => (ExpressionSyntax)
                    SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(b)));
                var arrayExpr = SyntaxFactory.ArrayCreationExpression(
                    SyntaxFactory.ArrayType(
                        SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.ByteKeyword)),
                        SyntaxFactory.SingletonList(SyntaxFactory.ArrayRankSpecifier())))
                    .WithInitializer(SyntaxFactory.InitializerExpression(
                        SyntaxKind.ArrayInitializerExpression,
                        SyntaxFactory.SeparatedList(byteExprs)));
                var stateCreation = SyntaxFactory.ObjectCreationExpression(
                    SyntaxFactory.ParseTypeName(Consts.ComposableArgumentsDefaultState.FullName))
                    .WithArgumentList(SyntaxFactory.ArgumentList(
                        SyntaxFactory.SingletonSeparatedList(
                            SyntaxFactory.Argument(arrayExpr))));
                defaultStateArg = SyntaxFactory.Argument(stateCreation);
            }

            var processedArgsArray = processedArgs.ToArray();

            var allArgs = new List<ArgumentSyntax>();
            for (int i = 0; i < parameterInfos.Length; i++)
            {
                int argIdx = ArgumentResolver.FindArgumentIndex(invocationExpression.ArgumentList.Arguments, i, parameterInfos[i].Name);

                if (argIdx >= 0)
                {
                    allArgs.Add(processedArgsArray[argIdx]);
                }
                else
                {
                    var paramType = parameterInfos[i].Type;
                    ExpressionSyntax defaultExpr = paramType != null
                        ? SyntaxFactory.DefaultExpression(
                            SyntaxFactory.ParseTypeName(paramType.ToDisplayString(
                                SymbolDisplayFormat.FullyQualifiedFormat
                                    .WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Included))))
                        : SyntaxFactory.LiteralExpression(SyntaxKind.DefaultLiteralExpression);
                    allArgs.Add(SyntaxFactory.Argument(defaultExpr));
                }
            }

            allArgs.Add(SyntaxFactory.Argument(SyntaxFactory.IdentifierName(options.ContextVarName)));
            allArgs.Add(SyntaxFactory.Argument(changedArg));
            allArgs.Add(defaultStateArg);

            ArgumentListSyntax newArgs = SyntaxFactory.ArgumentList(
                SyntaxFactory.SeparatedList(allArgs));
            session.MarkComposableProcessed();

            invocationExpression = ReplaceWithFullQualifiedName(invocationExpression, methodSymbol);
            MemberAccessExpressionSyntax? lastmemberAccess = invocationExpression.DescendantNodes().OfType<MemberAccessExpressionSyntax>().FirstOrDefault();
            if (lastmemberAccess != null)
            {
                string lastAccessedMemberName = lastmemberAccess.Name.ToFullString();
                string newAccessMemberName = $"{options.BuildersClassName}.{lastAccessedMemberName}";
                invocationExpression = (InvocationExpressionSyntax)ReplaceLastMemberAccess(invocationExpression, lastAccessedMemberName, newAccessMemberName);
                return invocationExpression.WithArgumentList(newArgs);
            }
            context.Diagnostics.Report(DiagnosticInfo.Create(
                DiagnosticDescriptors.DNC008_MemberAccessNotFound,
                invocationExpression.GetLocation()));
            return invocationExpression.WithArgumentList(newArgs);
        }

        private static InvocationExpressionSyntax ReplaceWithFullQualifiedName(InvocationExpressionSyntax node, IMethodSymbol methodSymbol)
        {
            var typeName = methodSymbol.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Included));

            SimpleNameSyntax newIdentifierName = default;
            if (methodSymbol.TypeArguments.Any())
            {
                newIdentifierName = SyntaxFactory.GenericName(methodSymbol.Name)
                        .WithTypeArgumentList(
                            SyntaxFactory.TypeArgumentList(SyntaxFactory.SeparatedList(methodSymbol.TypeArguments.Select(a =>
                            {
                                return SyntaxFactory.ParseTypeName(a.ToDisplayString());
                            }))));
            }
            else
            {
                newIdentifierName = SyntaxFactory.IdentifierName(methodSymbol.Name);
            }
            var newQualifiedName = SyntaxFactory.ParseName(typeName);

            ExpressionSyntax newExpression;
            if (newQualifiedName is QualifiedNameSyntax qns)
            {
                newExpression = SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    qns.Left,
                    (IdentifierNameSyntax)qns.Right);
            }
            else
            {
                newExpression = SyntaxFactory.IdentifierName(typeName);
            }

            newExpression = SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                newExpression,
                newIdentifierName);

            return node.WithExpression(newExpression);
        }

        private static SyntaxNode ReplaceLastMemberAccess(SyntaxNode root, string oldMemberName, string newMemberPath)
        {
            var memberAccesses = root.DescendantNodes()
                .OfType<MemberAccessExpressionSyntax>()
                .Where(m => m.Name.ToString() == oldMemberName)
                .ToList();

            var lastMemberAccesses = memberAccesses
                .Where(m => !(m.Parent is MemberAccessExpressionSyntax))
                .ToList();

            if (!lastMemberAccesses.Any())
                return root;

            var newRoot = root;
            foreach (var memberAccess in lastMemberAccesses)
            {
                var newExpression = BuildNewMemberAccess(memberAccess.Expression, newMemberPath)
                    .WithTriviaFrom(memberAccess);

                newRoot = newRoot.ReplaceNode(memberAccess, newExpression);
            }

            return newRoot;
        }

        private static ExpressionSyntax BuildNewMemberAccess(ExpressionSyntax leftmost, string newPath)
        {
            var parts = newPath.Split('.');
            ExpressionSyntax current = leftmost;

            foreach (var part in parts)
            {
                current = SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    current,
                    SyntaxFactory.IdentifierName(part));
            }

            return current;
        }
    }
}
