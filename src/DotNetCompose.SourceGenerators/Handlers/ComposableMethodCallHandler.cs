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
            RewriterOptions options = context.Options;
            MethodGenerationContext methodCtx = context.MethodCtx;
            RewriterSession session = context.Session;
            SemanticModel semanticModel = context.SemanticModel;

            ImmutableArray<MethodParameterInfo> parameterInfos = methodSymbol.GetParametersInfos(semanticModel);
            bool targetIsReadOnly = methodSymbol.IsReadOnlyComposableFunction();
            if (context.IsReadOnly && !targetIsReadOnly)
            {
                context.Diagnostics.Report(DiagnosticInfo.Create(
                    DiagnosticDescriptors.DNC016_NonReadOnlyCall,
                    invocationExpression.GetLocation(),
                    methodSymbol.Name));
            }

            using ListPoolObject<(ArgumentSyntax Argument, bool IsComposable, bool IsReadOnly, bool IsInline, MethodParameterInfo? Parameter)> arguments =
                ListPool<(ArgumentSyntax, bool, bool, bool, MethodParameterInfo?)>.Get();
            arguments.AddRange(invocationExpression.ArgumentList.Arguments.Select((arg, index) =>
            {
                MethodParameterInfo? parameter;
                if (arg.NameColon != null)
                {
                    parameter = parameterInfos.FirstOrDefault(a => a.Name == arg.NameColon.Name.Identifier.ValueText);
                }
                else
                {
                    parameter = index < parameterInfos.Length ? parameterInfos[index] : null;
                }
                return (
                    arg,
                    parameter?.IsComposable ?? false,
                    parameter?.IsReadOnly ?? false,
                    parameter?.IsInline ?? false,
                    parameter);
            }));

            IEnumerable<ArgumentSyntax> processedArgs = arguments.Select(a =>
            {
                ArgumentSyntax arg = a.Argument;
                bool isComposable = a.IsComposable;
                if (!isComposable)
                    return arg;
                if (context.IsReadOnly && !a.IsReadOnly)
                {
                    context.Diagnostics.Report(DiagnosticInfo.Create(
                        DiagnosticDescriptors.DNC016_NonReadOnlyCall,
                        arg.GetLocation(),
                        methodSymbol.Name));
                    return arg;
                }

                if (arg.Expression is IdentifierNameSyntax identifierName)
                {
                    ISymbol? argumentSymbol = semanticModel.GetSymbolInfo(identifierName).Symbol;
                    if (argumentSymbol is IMethodSymbol argumentMethod)
                    {
                        context.Diagnostics.Report(DiagnosticInfo.Create(
                            a.IsReadOnly
                                ? DiagnosticDescriptors.DNC017_UnverifiableReadOnlyDelegate
                                : DiagnosticDescriptors.DNC005_DirectComposableReference,
                            identifierName.GetLocation(),
                            a.IsReadOnly ? a.Parameter?.Name ?? string.Empty : argumentMethod.Name));
                        return arg;
                    }
                    if (argumentSymbol is IParameterSymbol parameterSymbol)
                    {
                        MethodParameterInfo? sourceParameter = methodCtx.Parameters
                            .FirstOrDefault(parameter => parameter.Name == parameterSymbol.Name);
                        if (a.IsReadOnly && !parameterSymbol.IsReadOnlyComposableParameter())
                        {
                            context.Diagnostics.Report(DiagnosticInfo.Create(
                                DiagnosticDescriptors.DNC017_UnverifiableReadOnlyDelegate,
                                identifierName.GetLocation(),
                                a.Parameter?.Name ?? string.Empty));
                        }
                        if (!a.IsInline && sourceParameter?.IsInline == true)
                        {
                            context.Diagnostics.Report(DiagnosticInfo.Create(
                                DiagnosticDescriptors.DNC021_InlineComposableEscape,
                                identifierName.GetLocation(),
                                sourceParameter.Name));
                        }
                        return arg;
                    }
                }

                ImmutableArray<ParameterSyntax> lambdaParameters;
                bool isCaptureAnything = false;
                CSharpSyntaxNode newBody = default;
                if (arg.Expression is SimpleLambdaExpressionSyntax simpleLambdaExpression)
                {
                    lambdaParameters = ImmutableArray.Create<ParameterSyntax>(simpleLambdaExpression.Parameter);
                    DataFlowAnalysis analizeInfo = semanticModel.AnalyzeDataFlow(simpleLambdaExpression.Body);
                    isCaptureAnything = analizeInfo.CapturedInside.Length > 0;
                    using (session.EnterReadOnlyScope(a.IsReadOnly))
                        newBody = (CSharpSyntaxNode)context.NodeTransformer.Transform(simpleLambdaExpression.Body);
                }
                else if (arg.Expression is ParenthesizedLambdaExpressionSyntax parenthesizedLambdaExpression)
                {
                    lambdaParameters = parenthesizedLambdaExpression.ParameterList.Parameters.ToImmutableArray();
                    DataFlowAnalysis analizeInfo = semanticModel.AnalyzeDataFlow(parenthesizedLambdaExpression.Body);
                    isCaptureAnything = analizeInfo.CapturedInside.Length > 0;
                    using (session.EnterReadOnlyScope(a.IsReadOnly))
                        newBody = (CSharpSyntaxNode)context.NodeTransformer.Transform(parenthesizedLambdaExpression.Body);
                }
                else
                {
                    context.Diagnostics.Report(DiagnosticInfo.Create(
                        a.IsReadOnly
                            ? DiagnosticDescriptors.DNC017_UnverifiableReadOnlyDelegate
                            : DiagnosticDescriptors.DNC006_UnrecognizedLambda,
                        arg.Expression.GetLocation(),
                        a.IsReadOnly ? new object[] { a.Parameter?.Name ?? string.Empty } : Array.Empty<object>()));
                    return arg;
                }

                ImmutableArray<(string Type, string Name)> argTypes = lambdaParameters.Select(item =>
                {
                    IParameterSymbol s = semanticModel.GetDeclaredSymbol(item);
                    return (
                        Type: s.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        Name: s.Name);
                }).ToImmutableArray();

                ImmutableArray<(string Type, string Name)> newArgs = argTypes.AddRange(new (string Type, string Name)[] {
                    (Consts.ComposeContext.FullName, options.ContextVarName),
                    (Consts.ComposableArgumentsState.FullName, options.ChangedVarName),
                    (Consts.ComposableArgumentsDefaultState.FullName, Consts.Rewriter.DefaultParamName),
                });

                ParameterListSyntax newParamList = SyntaxFactory.ParameterList(
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
                    if (a.IsInline)
                    {
                        return arg.WithExpression(
                            SyntaxFactory.ParenthesizedLambdaExpression(newParamList, newBody));
                    }

                    TypeSyntax variableType = default;
                    if (argTypes.Length == 0)
                    {
                        variableType = SyntaxFactory.IdentifierName(Consts.ComposableAction.FullName);
                    }
                    else
                    {
                        variableType = SyntaxFactory.GenericName(
                            SyntaxFactory.Identifier(Consts.ComposableAction.FullName),
                            SyntaxFactory.TypeArgumentList(
                                SyntaxFactory.SeparatedList(
                                    argTypes.Select(t => SyntaxFactory.ParseTypeName(t.Type)))));
                    }
                    variableType = variableType.WithTrailingSpace();

                    string helperName = a.IsReadOnly ? "GetReadonlyLambda" : "GetLambda";
                    InvocationExpressionSyntax wrappedLambdaExpression = SyntaxFactoryHelpers.CreateMethodCallSyntaxWithArgs(
                        "ComposeHelpers",
                        helperName,
                        SyntaxFactory.IdentifierName(options.ContextVarName),
                        SyntaxFactoryHelpers.CreateIntLiteral(session.NextLambdaKey()),
                        SyntaxFactory.ParenthesizedLambdaExpression(
                            SyntaxFactory.ParameterList(),
                            SyntaxFactory.Block(
                                SyntaxFactory.LocalDeclarationStatement(
                                    SyntaxFactory.VariableDeclaration(variableType).AddVariables(
                                        SyntaxFactory.VariableDeclarator("a").WithInitializer(
                                            SyntaxFactory.EqualsValueClause(
                                                SyntaxFactory.ParenthesizedLambdaExpression(newParamList, newBody)))
                                            .WithLeadingSpace())
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
                                                                SyntaxFactory.SeparatedList(
                                                                    argTypes.Select(t => SyntaxFactory.ParseTypeName(t.Type)))
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
                    bool bodyConversionFailed = newBody is not BlockSyntax and not ArrowExpressionClauseSyntax and not ExpressionSyntax;
                    BlockSyntax newBodyBlockSyntax = newBody switch
                    {
                        BlockSyntax block => block,
                        ArrowExpressionClauseSyntax arrowExpression => SyntaxFactory.Block(
                            SyntaxFactory.ExpressionStatement(arrowExpression.Expression)),
                        ExpressionSyntax expressionBody => SyntaxFactory.Block(SyntaxFactory.ExpressionStatement(expressionBody)),
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

            ExpressionSyntax changedArg = ArgumentResolver.BuildChangedArg(
                parameterInfos,
                invocationExpression.ArgumentList.Arguments,
                methodCtx,
                semanticModel);

            int defaultCount = parameterInfos.Count(p => p.DefaultProviderType != null);
            bool anyShouldUseDefault = false;
            byte[] defaultStateBytes = new byte[defaultCount];
            for (int i = 0; i < methodSymbol.Parameters.Length; i++)
            {
                MethodParameterInfo paramInfo = parameterInfos[i];
                if (paramInfo.DefaultProviderType == null) continue;

                int argIdx = ArgumentResolver.FindArgumentIndex(invocationExpression.ArgumentList.Arguments, i, paramInfo.Name);

                if (argIdx >= 0)
                {
                    ExpressionSyntax argExpr = invocationExpression.ArgumentList.Arguments[argIdx].Expression;
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
                IEnumerable<ExpressionSyntax> byteExprs = defaultStateBytes.Select(b => (ExpressionSyntax)
                    SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(b)));
                StackAllocArrayCreationExpressionSyntax arrayExpr = SyntaxFactory.StackAllocArrayCreationExpression(
                    SyntaxFactory.ArrayType(
                        SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.ByteKeyword)),
                        SyntaxFactory.SingletonList(SyntaxFactory.ArrayRankSpecifier())),
                    SyntaxFactory.InitializerExpression(
                        SyntaxKind.ArrayInitializerExpression,
                        SyntaxFactory.SeparatedList(byteExprs)));
                ObjectCreationExpressionSyntax stateCreation = SyntaxFactory.ObjectCreationExpression(
                    SyntaxFactory.ParseTypeName(Consts.ComposableArgumentsDefaultState.FullName))
                    .WithArgumentList(SyntaxFactory.ArgumentList(
                        SyntaxFactory.SingletonSeparatedList(
                            SyntaxFactory.Argument(arrayExpr))));
                defaultStateArg = SyntaxFactory.Argument(stateCreation);
            }

            ArgumentSyntax[] processedArgsArray = processedArgs.ToArray();

            List<ArgumentSyntax> allArgs = new List<ArgumentSyntax>();
            for (int i = 0; i < parameterInfos.Length; i++)
            {
                int argIdx = ArgumentResolver.FindArgumentIndex(invocationExpression.ArgumentList.Arguments, i, parameterInfos[i].Name);

                if (argIdx >= 0)
                {
                    allArgs.Add(processedArgsArray[argIdx]);
                }
                else
                {
                    ExpressionSyntax defaultExpr = BuildDefaultExpression(methodSymbol.Parameters[i], parameterInfos[i].Type);
                    allArgs.Add(SyntaxFactory.Argument(defaultExpr));
                }
            }

            allArgs.Add(SyntaxFactory.Argument(SyntaxFactory.IdentifierName(options.ContextVarName)));
            allArgs.Add(SyntaxFactory.Argument(changedArg));
            allArgs.Add(defaultStateArg);

            ArgumentListSyntax newArgs = SyntaxFactory.ArgumentList(
                SyntaxFactory.SeparatedList(allArgs));
            if (!targetIsReadOnly)
                session.MarkComposableProcessed();

            if (!methodSymbol.IsStatic)
                return invocationExpression.WithArgumentList(newArgs);

            invocationExpression = ReplaceWithFullQualifiedName(invocationExpression, methodSymbol);
            MemberAccessExpressionSyntax? lastmemberAccess = invocationExpression
                .DescendantNodes()
                .OfType<MemberAccessExpressionSyntax>()
                .FirstOrDefault();
            if (lastmemberAccess != null)
            {
                string lastAccessedMemberName = lastmemberAccess.Name.ToFullString();
                string newAccessMemberName = $"{options.BuildersClassName}.{lastAccessedMemberName}";
                invocationExpression = (InvocationExpressionSyntax)ReplaceLastMemberAccess(
                    invocationExpression,
                    lastAccessedMemberName,
                    newAccessMemberName);
                return invocationExpression.WithArgumentList(newArgs);
            }
            context.Diagnostics.Report(DiagnosticInfo.Create(
                DiagnosticDescriptors.DNC008_MemberAccessNotFound,
                invocationExpression.GetLocation()));
            return invocationExpression.WithArgumentList(newArgs);
        }

        private static ExpressionSyntax BuildDefaultExpression(IParameterSymbol parameter, ITypeSymbol? parameterType)
        {
            if (!parameter.HasExplicitDefaultValue || parameterType == null)
            {
                return parameterType == null
                    ? SyntaxFactory.LiteralExpression(SyntaxKind.DefaultLiteralExpression)
                    : SyntaxFactory.DefaultExpression(SyntaxFactory.ParseTypeName(parameterType.ToDisplayString(
                        SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Included))));
            }

            object? value = parameter.ExplicitDefaultValue;
            if (value == null)
            {
                if (parameterType.IsValueType && parameter.NullableAnnotation != NullableAnnotation.Annotated)
                    return SyntaxFactory.DefaultExpression(SyntaxFactory.ParseTypeName(parameterType.ToDisplayString(
                        SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Included))));
                return SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression);
            }
            if (value is string text)
            {
                return SyntaxFactory.LiteralExpression(
                    SyntaxKind.StringLiteralExpression,
                    SyntaxFactory.Literal(text));
            }
            if (value is char character)
            {
                return SyntaxFactory.LiteralExpression(
                    SyntaxKind.CharacterLiteralExpression,
                    SyntaxFactory.Literal(character));
            }
            if (value is bool boolean)
            {
                return SyntaxFactory.LiteralExpression(
                    boolean ? SyntaxKind.TrueLiteralExpression : SyntaxKind.FalseLiteralExpression);
            }

            string literal = SymbolDisplay.FormatPrimitive(value, quoteStrings: true, useHexadecimalNumbers: false);
            ExpressionSyntax expression = SyntaxFactory.ParseExpression(literal);
            if (parameterType.TypeKind == TypeKind.Enum)
            {
                TypeSyntax enumType = SyntaxFactory.ParseTypeName(parameterType.ToDisplayString(
                    SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Included)));
                expression = SyntaxFactory.CastExpression(enumType, expression);
            }
            return expression;
        }

        private static InvocationExpressionSyntax ReplaceWithFullQualifiedName(InvocationExpressionSyntax node, IMethodSymbol methodSymbol)
        {
            string typeName = methodSymbol.ContainingType.ToDisplayString(
                SymbolDisplayFormat.FullyQualifiedFormat
                    .WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Included));

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
            NameSyntax newQualifiedName = SyntaxFactory.ParseName(typeName);

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
            List<MemberAccessExpressionSyntax> memberAccesses = root.DescendantNodes()
                .OfType<MemberAccessExpressionSyntax>()
                .Where(m => m.Name.ToString() == oldMemberName)
                .ToList();

            List<MemberAccessExpressionSyntax> lastMemberAccesses = memberAccesses
                .Where(m => !(m.Parent is MemberAccessExpressionSyntax))
                .ToList();

            if (!lastMemberAccesses.Any())
                return root;

            SyntaxNode newRoot = root;
            foreach (MemberAccessExpressionSyntax memberAccess in lastMemberAccesses)
            {
                ExpressionSyntax newExpression = BuildNewMemberAccess(memberAccess.Expression, newMemberPath)
                    .WithTriviaFrom(memberAccess);

                newRoot = newRoot.ReplaceNode(memberAccess, newExpression);
            }

            return newRoot;
        }

        private static ExpressionSyntax BuildNewMemberAccess(ExpressionSyntax leftmost, string newPath)
        {
            string[] parts = newPath.Split('.');
            ExpressionSyntax current = leftmost;

            foreach (string part in parts)
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
