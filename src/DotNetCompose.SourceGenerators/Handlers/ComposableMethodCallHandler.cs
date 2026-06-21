using DotNetCompose.SourceGenerators;
using DotNetCompose.SourceGenerators.Extensions;
using DotNetCompose.SourceGenerators.Rewriters;
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

namespace DotNetCompose.SourceGenerators.Handlers
{
    internal class ComposableMethodCallHandler : IMethodCallHandler
    {
        public InterceptionResult Handle(
            ExpressionSyntax expression,
            IMethodSymbol methodSymbol,
            MethodCallHandlerContext context,
            out SyntaxNode? replacement)
        {
            replacement = null;

            if (methodSymbol.MethodKind != MethodKind.Ordinary)
                return InterceptionResult.Continue;
            if (!methodSymbol.IsComposableFunction())
                return InterceptionResult.Continue;
            if (expression is not InvocationExpressionSyntax invocationExpression)
                return InterceptionResult.Continue;

            replacement = ProcessComposableCall(invocationExpression, methodSymbol, context);
            return InterceptionResult.Handled;
        }

        private InvocationExpressionSyntax ProcessComposableCall(
            InvocationExpressionSyntax invocationExpression,
            IMethodSymbol methodSymbol,
            MethodCallHandlerContext context)
        {
            var ctx = context.GeneratorContext;
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
                        throw new NotSupportedException("Composable method referencing is not supported");
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
                    throw new NotSupportedException();

                ImmutableArray<(string Type, string Name)> argTypes = lambdaParameters.Select(item =>
                {
                    IParameterSymbol s = semanticModel.GetDeclaredSymbol(item);
                    return (Type: s.Type.GetFullMetadataName(), Name: s.Name);
                }).ToImmutableArray();

                ImmutableArray<(string Type, string Name)> newArgs = argTypes.AddRange(new (string Type, string Name)[] {
                    (Consts.ComposeContext.FullName, ctx.ContextVarName),
                    (Consts.ComposableArgumentsState.FullName, ctx.ChangedVarName),
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
                        SyntaxFactory.IdentifierName(ctx.ContextVarName),
                        SyntaxFactoryHelpers.CreateIntLiteral(ctx.GetNextLambdaKey()),
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
                    string name = ctx.GetNextLambdaName();

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

                    ctx.AddStoredLambda(new StoredLambda(name, newArgs, lambdaMethodDeclaration));

                    return arg.WithExpression(SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                                                        SyntaxFactory.IdentifierName(ctx.StoredLambdaIdentifierName),
                                                        SyntaxFactory.IdentifierName(name)));
                }
            });

            ExpressionSyntax changedArg = BuildChangedArg(parameterInfos, invocationExpression.ArgumentList.Arguments, ctx);

            int defaultCount = parameterInfos.Count(p => p.DefaultProviderType != null);
            bool anyShouldUseDefault = false;
            byte[] defaultStateBytes = new byte[defaultCount];
            for (int i = 0; i < methodSymbol.Parameters.Length; i++)
            {
                var paramInfo = parameterInfos[i];
                if (paramInfo.DefaultProviderType == null) continue;

                int argIdx = -1;
                for (int j = 0; j < invocationExpression.ArgumentList.Arguments.Count; j++)
                {
                    var invArg = invocationExpression.ArgumentList.Arguments[j];
                    if (invArg.NameColon != null)
                    {
                        if (invArg.NameColon.Name.Identifier.ValueText == paramInfo.Name)
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
                int argIdx = -1;
                for (int j = 0; j < invocationExpression.ArgumentList.Arguments.Count; j++)
                {
                    var invArg = invocationExpression.ArgumentList.Arguments[j];
                    if (invArg.NameColon != null)
                    {
                        if (invArg.NameColon.Name.Identifier.ValueText == parameterInfos[i].Name)
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

            allArgs.Add(SyntaxFactory.Argument(SyntaxFactory.IdentifierName(ctx.ContextVarName)));
            allArgs.Add(SyntaxFactory.Argument(changedArg));
            allArgs.Add(defaultStateArg);

            ArgumentListSyntax newArgs = SyntaxFactory.ArgumentList(
                SyntaxFactory.SeparatedList(allArgs));
            ctx.ComposableProcessed();

            invocationExpression = ReplaceWithFullQualifiedName(invocationExpression, methodSymbol);
            MemberAccessExpressionSyntax? lastmemberAccess = invocationExpression.DescendantNodes().OfType<MemberAccessExpressionSyntax>().FirstOrDefault();
            if (lastmemberAccess != null)
            {
                string lastAccessedMemberName = lastmemberAccess.Name.ToFullString();
                string newAccessMemberName = $"{ctx.BuildersClassName}.{lastAccessedMemberName}";
                invocationExpression = (InvocationExpressionSyntax)ReplaceLastMemberAccess(invocationExpression, lastAccessedMemberName, newAccessMemberName);
                return invocationExpression.WithArgumentList(newArgs);
            }
            throw new NotSupportedException();
        }

        private ExpressionSyntax BuildChangedArg(
            ImmutableArray<MethodParameterInfo> calleeParams,
            SeparatedSyntaxList<ArgumentSyntax> args,
            ComposableMethodGeneratorContext ctx)
        {
            if (ctx.HasUnstableParam)
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
                    var callerParams = ctx.MethodParameters;
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
