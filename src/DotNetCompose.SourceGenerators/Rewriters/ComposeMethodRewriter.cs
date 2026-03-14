using DotNetCompose.SourceGenerators.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Xml.Linq;
using static DotNetCompose.SourceGenerators.ComposableMethodGeneratorContext;
using static DotNetCompose.SourceGenerators.Extensions.MethodDeclarationSyntaxExtensions;
#nullable enable
namespace DotNetCompose.SourceGenerators.Rewriters
{

    internal class ComposeMethodRewriter : CSharpSyntaxRewriter
    {
        private ComposeMethodRewriter(ComposableMethodGeneratorContext ctx, SemanticModel semanticModel)
        {
            _semanticModel = semanticModel;
            _ctx = ctx;
        }

        private SemanticModel _semanticModel;
        private ComposableMethodGeneratorContext _ctx;
        private string _suffix = "Generated";
        public override SyntaxNode VisitMethodDeclaration(MethodDeclarationSyntax method)
        {
            var sourceLocationAnnotation = method.CreateLocationSyntaxAnnotation();

            _ctx.MethodParameters = method.GetParametersInfos(_semanticModel);
            _ctx.MethodModifiers = method.Modifiers;
            //string newName = string.Format("{0}_{1}", method.Identifier.Text, _suffix);
            bool hasAnyComposables = _ctx.MethodParameters.Any(p => p.IsComposable);

            ParameterListSyntax newParameterList = method.ParameterList;
            if (hasAnyComposables)
            {
                newParameterList = ReplaceAllComposableParameters(method, _semanticModel);
            }
            newParameterList = AppendComposableContextrelatedParameters(newParameterList, _semanticModel);

            MethodDeclarationSyntax newMethod = method
                //.WithIdentifier(SyntaxFactory.Identifier(newName))
                .WithParameterList(newParameterList)
                .WithAttributeLists(ReplaceComposableAttribute(method.AttributeLists, _semanticModel));

            if (method.Body != null)
            {
                BlockSyntax transformedBody = base.Visit(method.Body) as BlockSyntax;
                newMethod = newMethod.WithBody(transformedBody);
            }
            else if (method.ExpressionBody != null)
            {
                // ArrowExpressionClauseSyntax not supported
                throw new NotSupportedException();
            }

            if (sourceLocationAnnotation != null)
                newMethod = newMethod.WithAdditionalAnnotations(sourceLocationAnnotation);

            return newMethod;
        }

        private ParameterListSyntax AppendComposableContextrelatedParameters(ParameterListSyntax paramList, SemanticModel semanticModel)
        {
            SeparatedSyntaxList<ParameterSyntax> newArguments = paramList.Parameters.AddRange(new ParameterSyntax[]
                    {
                SyntaxFactory.Parameter(default,
                    default,
                    SyntaxFactory.ParseTypeName(Consts.ComposeContext.FullName).WithTrailingSpace(),
                    SyntaxFactory.Identifier(_ctx.ContextVarName),
                    default),

                SyntaxFactory.Parameter(default,
                    default,
                    SyntaxFactory.ParseTypeName("int").WithTrailingSpace() ,     // SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.IntKeyword))
                    SyntaxFactory.Identifier("__changed"),
                    default),
                    });

            return paramList.WithParameters(newArguments);
        }

        public override SyntaxNode VisitBlock(BlockSyntax node)
        {
            if (node.Parent is MethodDeclarationSyntax)
            {
                return VisitMethodDeclarationBlock(node);
            }
            return base.VisitBlock(node);
        }

        /* 
         * TReturnType FunctionName(ARGS)
         * {
         *     STATEMENTS
         * }
         */
        private SyntaxNode VisitMethodDeclarationBlock(BlockSyntax node)
        {
            string contextVarName = _ctx.ContextVarName;
            using ListPoolObject<StatementSyntax> syntaxList = ListPool<StatementSyntax>.Get();

            syntaxList.Add(SyntaxFactoryHelpers.CreateSafeMethodCallOnVariableWithArgs(
                contextVarName,
                Consts.ComposeContext.StartGroupMethod,
                SyntaxFactoryHelpers.CreateIntLiteral(_ctx.InitialGroupId)));

            BlockSyntax newBlock = base.VisitBlock(node) as BlockSyntax;

            syntaxList.AddRange(newBlock.Statements);

            syntaxList.Add(SyntaxFactoryHelpers.CreateSafeMethodCallOnVariableWithArgs(
                contextVarName,
                Consts.ComposeContext.EndGroupMethod,
                SyntaxFactoryHelpers.CreateIntLiteral(_ctx.InitialGroupId)));

            return newBlock.WithStatements(SyntaxFactory.List(syntaxList));
        }



        public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            IMethodSymbol? methodSymbol = _semanticModel.GetSymbolInfo(node).Symbol as IMethodSymbol;
            if (methodSymbol == null)
                return base.VisitInvocationExpression(node);

            ExpressionSyntax processedMethodCall = ProcessInvokeMethodExpression(node, methodSymbol);
            if (processedMethodCall != null)
                return processedMethodCall;
            else
                return base.VisitInvocationExpression(node);
        }
        public override SyntaxNode? VisitConditionalAccessExpression(ConditionalAccessExpressionSyntax node)
        {
            IMethodSymbol? methodSymbol = _semanticModel.GetSymbolInfo(node.WhenNotNull).Symbol as IMethodSymbol;
            if (methodSymbol == null)
                return base.VisitConditionalAccessExpression(node);
            ExpressionSyntax processedMethodCall = ProcessInvokeMethodExpression(node, methodSymbol);
            if (processedMethodCall != null)
                return processedMethodCall;
            else
                return base.VisitConditionalAccessExpression(node);
        }

        private ExpressionSyntax? ProcessInvokeMethodExpression(ExpressionSyntax expression, IMethodSymbol methodSymbol)
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

        public override SyntaxNode VisitIfStatement(IfStatementSyntax node)
        {
            using var ifProcessingHanler = _ctx.WithIfProcessing();
            IEnumerable<StatementSyntax>? ifStatementsToProcesss = null;
            if (node.Statement is BlockSyntax blockSyntax)
            {
                ifStatementsToProcesss = blockSyntax.Statements;
            }
            else if (node.Statement is ExpressionStatementSyntax expressionStatementSyntax)
            {
                ifStatementsToProcesss = new StatementSyntax[] { expressionStatementSyntax };
            }
            if (ifStatementsToProcesss == null)
                throw new NotSupportedException();
            using ListPoolObject<StatementSyntax> ifOutStatements = ListPool<StatementSyntax>.Get();
            foreach (StatementSyntax statement in ifStatementsToProcesss)
            {
                StatementSyntax? newStatement = base.Visit(statement) as StatementSyntax;
                if (newStatement != null)
                    ifOutStatements.Add(newStatement);
            }

            IEnumerable<StatementSyntax> elseStatementsToProcesss = null;
            bool isElseIfBlock = false;
            if (node.Else?.Statement is BlockSyntax elseBlockSyntax)
            {
                elseStatementsToProcesss = elseBlockSyntax.Statements;
            }
            else if (node.Else?.Statement is ExpressionStatementSyntax elseExpressionStatementSyntax)
            {
                elseStatementsToProcesss = new StatementSyntax[] { elseExpressionStatementSyntax };
            }
            else if (node.Else?.Statement is IfStatementSyntax innerIfStatements)
            {
                elseStatementsToProcesss = new StatementSyntax[] { innerIfStatements };
                isElseIfBlock = true;
            }

            using ListPoolObject<StatementSyntax> elseOutStatements = ListPool<StatementSyntax>.Get();
            if (elseStatementsToProcesss != null)
            {
                foreach (StatementSyntax statements in elseStatementsToProcesss)
                {
                    StatementSyntax? newStatement = base.Visit(statements) as StatementSyntax;
                    if (newStatement != null)
                        elseOutStatements.Add(newStatement);
                }
            }

            if (!_ctx.WasGeneratedComposableFunctionWithinConditionalBlocks)
                return base.VisitIfStatement(node);

            int ifGroupId = _ctx.GetNextGroupId();
            ElseClauseSyntax? newElseClauseSyntax = node.Else;
            if (elseOutStatements.Any())
            {
                int elseGroupId = _ctx.GetNextGroupId();
                ExpressionStatementSyntax elseGroupStartStatement = SyntaxFactoryHelpers.CreateSafeMethodCallOnVariableWithArgs(_ctx.ContextVarName,
                    Consts.ComposeContext.StartRestartableGroupMethod,
                    SyntaxFactoryHelpers.CreateIntLiteral(elseGroupId))
                    .WithTrailingNewLine();
                ExpressionStatementSyntax elseGroupEndStatement = SyntaxFactoryHelpers.CreateSafeMethodCallOnVariableWithArgs(_ctx.ContextVarName,
                    Consts.ComposeContext.EndRestartableGroupMethod,
                    SyntaxFactoryHelpers.CreateIntLiteral(elseGroupId));

                if (isElseIfBlock)
                {
                    StatementSyntax statementSyntax = default;
                    if (elseOutStatements.Count == 1)
                        statementSyntax = elseOutStatements[0];
                    else
                        statementSyntax = SyntaxFactory.Block(elseOutStatements);

                    newElseClauseSyntax = node.Else
                            .WithStatement(statementSyntax)
                            .WithTrailingNewLine();
                }
                else
                {
                    newElseClauseSyntax = node.Else.WithStatement(SyntaxFactory.Block(
                            WrapStatementsWithGroupStartAndEndMethods(elseGroupStartStatement, elseOutStatements, elseGroupEndStatement)))
                        .WithTrailingNewLine();
                }
            }
            IfStatementSyntax newIfStatement = node.WithElse(newElseClauseSyntax);

            if (ifOutStatements.Any())
            {
                ExpressionStatementSyntax ifGroupStartStatement = SyntaxFactoryHelpers.CreateSafeMethodCallOnVariableWithArgs(_ctx.ContextVarName,
                        Consts.ComposeContext.StartRestartableGroupMethod,
                        SyntaxFactoryHelpers.CreateIntLiteral(ifGroupId))
                    .WithTrailingNewLine();

                ExpressionStatementSyntax ifGroupEndStatement = SyntaxFactoryHelpers.CreateSafeMethodCallOnVariableWithArgs(_ctx.ContextVarName,
                    Consts.ComposeContext.EndRestartableGroupMethod,
                    SyntaxFactoryHelpers.CreateIntLiteral(ifGroupId));

                newIfStatement = node.WithStatement(SyntaxFactory.Block(
                        WrapStatementsWithGroupStartAndEndMethods(ifGroupStartStatement, ifOutStatements, ifGroupEndStatement)))
                    .WithElse(newElseClauseSyntax)
                    .WithTrailingNewLine();
            }

            return newIfStatement;
        }
        public override SyntaxNode VisitForStatement(ForStatementSyntax forStatement)
        {
            IEnumerable<StatementSyntax>? statementsToProcess = null;
            if (forStatement.Statement is BlockSyntax block)
            {
                statementsToProcess = block.Statements;
            }
            else
                throw new NotSupportedException();
            if (statementsToProcess != null)
            {
                using ListPoolObject<StatementSyntax> outForStatements = ListPool<StatementSyntax>.Get();
                foreach (StatementSyntax processingStatement in statementsToProcess)
                {
                    SyntaxNode st = base.Visit(processingStatement);

                    if (st is StatementSyntax statement)
                        outForStatements.Add(statement);
                }
                return forStatement.WithStatement(SyntaxFactory.Block(outForStatements)).WithTrailingNewLine();
            }
            else
            {
                return base.VisitForStatement(forStatement);
            }
        }
        public override SyntaxNode VisitForEachStatement(ForEachStatementSyntax forEachStatement)
        {
            IEnumerable<StatementSyntax> statementsToProcess = null;
            if (forEachStatement.Statement is BlockSyntax block)
            {
                statementsToProcess = block.Statements;
            }
            else
                throw new NotSupportedException();
            if (statementsToProcess != null)
            {
                using ListPoolObject<StatementSyntax> outForEachStatements = ListPool<StatementSyntax>.Get();
                foreach (StatementSyntax processingStatement in statementsToProcess)
                {
                    SyntaxNode st = base.Visit(processingStatement);

                    if (st is StatementSyntax statement)
                        outForEachStatements.Add(statement);
                }
                return forEachStatement.WithStatement(SyntaxFactory.Block(outForEachStatements)).WithTrailingNewLine();
            }
            else
            {
                return base.VisitForEachStatement(forEachStatement);
            }
        }

        public override SyntaxNode? VisitExpressionStatement(ExpressionStatementSyntax node)
        {
            var locationAnnotation = node.CreateLocationSyntaxAnnotation();
            var processed = base.VisitExpressionStatement(node);
            if (locationAnnotation != null && processed != null)
                processed = processed.WithAdditionalAnnotations(locationAnnotation);
            return processed;
        }
        public override SyntaxNode? VisitVariableDeclaration(VariableDeclarationSyntax node)
        {
            var locationAnnotation = node.CreateLocationSyntaxAnnotation();
            var processed = base.VisitVariableDeclaration(node);
            if (locationAnnotation != null && processed != null)
                processed = processed.WithAdditionalAnnotations(locationAnnotation);
            return processed;
        }

        private ExpressionSyntax VisitComposableMethodCall(InvocationExpressionSyntax invocationExpression)
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

                    // If lambda captures anything, get new from ComposeContext
                    // otherwise store it in some static place and use

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
                            ("int", _ctx.ChangedVarName),
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
                    return arg;
                });

            ArgumentListSyntax newArgs = SyntaxFactory.ArgumentList(
                SyntaxFactory.SeparatedList<ArgumentSyntax>(processedArgs).AddRange(
                    new ArgumentSyntax[]{
                        SyntaxFactory.Argument(SyntaxFactory.IdentifierName(_ctx.ContextVarName)),
                        SyntaxFactory.Argument(SyntaxFactory.IdentifierName(_ctx.ChangedVarName)),
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
            // TODo replace mwthod call with Full Qualified Name
            if (invocationExpression.Expression is IdentifierNameSyntax identifierName)
            {
                SyntaxToken identifierToken = identifierName.Identifier;
                string methodName = identifierToken.Text;
                return SyntaxFactoryHelpers.CreateMethodCallSyntaxWithArgs(
                    _ctx.BuildersClassName,
                    methodName,
                    newArgs);
            }
            // if the expression is a member access (e.g., "obj.DoSomething()")
            else if (invocationExpression.Expression is MemberAccessExpressionSyntax memberAccess)
            {
                SyntaxToken identifierToken = memberAccess.Name.Identifier;
                string methodName = identifierToken.Text;

                //var builderMethodAccess = memberAccess.WithExpression(SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression));
                //invocationExpression = invocationExpression.WithExpression(builderMethodAccess);
                return invocationExpression.WithArgumentList(newArgs);
            }
            return invocationExpression.WithArgumentList(newArgs);
        }

        private InvocationExpressionSyntax ReplaceWithFullQualifiedName(InvocationExpressionSyntax node)
        {
            // Get semantic information about the invoked method
            var symbolInfo = _semanticModel.GetSymbolInfo(node);
            var symbol = symbolInfo.Symbol;

            if (symbol is IMethodSymbol methodSymbol)
            {
                // This part might be tricky if the method is static and called directly, 
                // but we want to replace the expression that leads to the method.
                // For a method call like `Console.WriteLine()`, the expression is `Console.WriteLine`.

                // A more robust way is often to rebuild the expression part:
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

                // Reconstruct the expression that the method is called on (the part before the parentheses)
                ExpressionSyntax newExpression;
                if (newQualifiedName is QualifiedNameSyntax qns)
                {
                    newExpression = SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        qns.Left,
                        (IdentifierNameSyntax)qns.Right);
                }
                else // It is likely a SimpleNameSyntax if it's the root of the type name
                {
                    newExpression = SyntaxFactory.IdentifierName(typeName);
                }

                newExpression = SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    newExpression,
                    newIdentifierName);

                // Replace the expression part of the invocation
                return node.WithExpression(newExpression);
            }

            return node;
        }
        public static SyntaxNode ReplaceLastMemberAccess(SyntaxNode root, string oldMemberName, string newMemberPath)
        {
            // Find all member access expressions
            var memberAccesses = root.DescendantNodes()
                .OfType<MemberAccessExpressionSyntax>()
                .Where(m => m.Name.ToString() == oldMemberName)
                .ToList();

            // Filter to only those that are the last in their chain
            var lastMemberAccesses = memberAccesses
                .Where(m => !(m.Parent is MemberAccessExpressionSyntax))
                .ToList();

            if (!lastMemberAccesses.Any())
                return root;

            // Replace each occurrence
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
        private ExpressionSyntax VisitComposableArgumentCall(ExpressionSyntax expression, DelegateMethodCallInfo delegateMethodCallInfo)
        {

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
                expression = invocationSyntax.WithArgumentList(newArguments);
            }
            else if (delegateMethodCallInfo.IsDirectCall)
            {
                InvocationExpressionSyntax invocation = expression as InvocationExpressionSyntax;
                expression = invocation.WithArgumentList(invocation.ArgumentList.AddArguments(
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

                expression = conditionalAccessExpression.WithWhenNotNull(
                    invocation.WithArgumentList(newArguments));
            }
            return expression;
        }


        /// <summary>
        /// (Param1, [Composable] Param2) -> (Param1, [ComposableGenerated] Param2ComposableAction)
        /// </summary>
        /// <param name="method"></param>
        /// <param name="semanticModel"></param>
        /// <returns></returns>
        private static ParameterListSyntax ReplaceAllComposableParameters(MethodDeclarationSyntax method, SemanticModel semanticModel)
        {
            var args = method.ParameterList.Parameters.Zip(
                    method.GetParametersInfos(semanticModel),
                    (parameter, paramInfo) => (parameter, paramInfo)
            );

            SeparatedSyntaxList<ParameterSyntax> newArguments = SyntaxFactory.SeparatedList(
                args.Select(s => (Syntax: s.parameter, ParamInfo: s.paramInfo))
                        .Select(oldParam =>
                        SyntaxFactory.Parameter(
                            oldParam.ParamInfo.IsComposable
                                ? ReplaceComposableActionParameterAttributes(oldParam.Syntax.AttributeLists, semanticModel)
                                : oldParam.Syntax.AttributeLists,
                            oldParam.Syntax.Modifiers,
                            oldParam.ParamInfo.IsComposable
                                ? SyntaxFactory.ParseTypeName(Consts.ComposableAction.FullNameWithGenericArguments(oldParam.ParamInfo.GenericArguments.Select(t => t.GetFullMetadataName()))).WithTrailingSpace()
                                : oldParam.Syntax.Type,
                            oldParam.Syntax.Identifier,
                            ReplaceDefaultArgumentValue(oldParam.Syntax.Default, oldParam.ParamInfo.IsComposable)
            )));


            return SyntaxFactory.ParameterList(newArguments);

        }

        private static SyntaxList<AttributeListSyntax> ReplaceComposableActionParameterAttributes(SyntaxList<AttributeListSyntax> attributeLists, SemanticModel semanticModel)
        {
            return SyntaxFactory.List(attributeLists.Select(aList =>
            {
                IEnumerable<AttributeSyntax> newAttributes = aList.Attributes.Select(attribute =>
                {
                    if (MethodDeclarationSyntaxExtensions.IsComposableAttribute(attribute, semanticModel))
                        return SyntaxFactory.Attribute(SyntaxFactory.IdentifierName(Consts.ComposableActionParameterFullTypeName));
                    else
                        return attribute;
                });
                return aList.WithAttributes(SyntaxFactory.SeparatedList(newAttributes));

            }));
        }

        private static EqualsValueClauseSyntax ReplaceDefaultArgumentValue(EqualsValueClauseSyntax defaultSyntax, bool isComposable)
        {
            if (defaultSyntax == null)
                return defaultSyntax;
            if (!isComposable)
                return defaultSyntax;

            return SyntaxFactory.EqualsValueClause(
                            SyntaxFactory.LiteralExpression(
                                SyntaxKind.DefaultLiteralExpression,
                                SyntaxFactory.Token(SyntaxKind.DefaultKeyword)
                            ));
        }

        private static SyntaxList<AttributeListSyntax> ReplaceComposableAttribute(SyntaxList<AttributeListSyntax> attributeLists, SemanticModel semanticModel)
        {
            AttributeSyntax[] editorNotVisibleAttribute = new AttributeSyntax[] { SyntaxFactoryHelpers.CreateEditorNotVisibleAttribute() };
            return SyntaxFactory.List(attributeLists.Select(aList =>
            {
                IEnumerable<AttributeSyntax> newAttributes = aList.Attributes.Select(attribute =>
                {
                    if (IsComposableAttributeSyntax(attribute))
                        return SyntaxFactory.Attribute(SyntaxFactory.IdentifierName(Consts.ComposeGeneratedAttributeFullTypeName));
                    else
                        return attribute;
                }).Concat(editorNotVisibleAttribute);
                return aList.WithAttributes(SyntaxFactory.SeparatedList(newAttributes));

            }));
        }


        private static bool IsComposableAttributeSyntax(AttributeSyntax s)
        {
            var name = s.Name.ToString();
            return name == Consts.ComposableAttributeFullName ||
                    name.EndsWith("Composable") ||
                    name.EndsWith("ComposableAttribute");
        }
        private static IEnumerable<StatementSyntax> WrapStatementsWithGroupStartAndEndMethods(ExpressionStatementSyntax groupStart,
                  IList<StatementSyntax> statements,
                  ExpressionStatementSyntax groupEnd)
        {
            yield return groupStart;
            foreach (var statement in statements)
            {
                yield return statement;
            }
            yield return groupEnd;
        }

        record DelegateMethodCallInfo(string RecieverObjectName, bool IsSimpleMemberAccessCall, bool IsDirectCall, bool IsNullSafeCall);
        private DelegateMethodCallInfo? GetDelegateMethodCallInfo(ExpressionSyntax expression, IMethodSymbol methodSymbol)
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
                    default
                        :
                        throw new NotSupportedException();
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
                        throw new NotSupportedException();
                }
            }
            else
                throw new NotSupportedException();


            return new DelegateMethodCallInfo(recieverObjectName, isSimpleMemberAccess, isDirectCall, isNullSafeCall);

        }

        internal static SyntaxNode? Rewrite(ComposableMethodGeneratorContext ctx, SemanticModel semanticModel, MethodDeclarationSyntax method)
        {
            ComposeMethodRewriter rewriter = new ComposeMethodRewriter(ctx, semanticModel);
            return rewriter.Visit(method);
        }
    }
}

