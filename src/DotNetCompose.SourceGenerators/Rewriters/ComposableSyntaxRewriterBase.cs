using DotNetCompose.SourceGenerators.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using static DotNetCompose.SourceGenerators.Consts;
using static DotNetCompose.SourceGenerators.Extensions.MethodDeclarationSyntaxExtensions;

#nullable enable
namespace DotNetCompose.SourceGenerators.Rewriters
{
    /*
         https://github.com/JetBrains/kotlin/blob/2e7e1fd40b1ff862f4b574b17c2034e441179958/plugins/compose/compiler-hosted/src/main/java/androidx/compose/compiler/plugins/kotlin/lower/ComposableFunctionBodyTransformer.kt#L71
       if all arguments are not stable we cannot skip whole function
       Actions and ComposableFunctions are stable*

        (1) if parameter is passed directly to some composable function as an argument 
                => CAlculate changed value for that function (bits + dirt flag)
        (2) if parameter is passed  to some composable function as an argument with some modifications 
                => Set changedValue for this argument to "Uncertain"
        (3) "Different" and "Same" will come from (1) calculation

    *KOTLIN*
    int $dirty = $changed;
      if (($changed & 14) == 0) {
         $dirty = $changed | ($composer.changed(testInt) ? 4 : 2);
     }
    if (($dirty & 731) == 146 && $composer.getSkipping()) {
         $composer.skipToGroupEnd();
      }
    means in C#:
    if(argN == UNCERTAIN)
       argN = Changed(argN)? Differ (not stable): Same (not stable)

    if(allArguments == SameAndStable && ctx.IsSkiping()) 
        ctx.Skip()


    StableMeans that we can't trust to the result of composer.Changed method because of the fact that composer use == operator. 
    e.g. Reference types can have same comparison results regardless of whether they changed or not


        if(allArgAreStable)
            GenerateDirtyFlagAndTrySkipWholeFunction()
            CalculateChnagedParamBasedOnDirtyFlag
        else
            CalculateChangedParamBasedOnChangedArgumnetOnly


        /////////////////
        if composable function have any unstable parameter (except composer,changed,defaults,any lambdas) 
            it must not generate code for parameter checking
        if composable function calls another composable function with unstable parameters
            it should pass Empty as a Changed parameter
        composable function that generates parameter checking should work with both 'Changed' argument Empty and Non Empty
 */
    internal abstract class ComposableSyntaxRewriterBase : CSharpSyntaxRewriter
    {
        protected ComposableSyntaxRewriterBase(ComposableMethodGeneratorContext ctx, SemanticModel semanticModel)
        {
            _semanticModel = semanticModel;
            _ctx = ctx;
        }
        protected SemanticModel _semanticModel;
        protected ComposableMethodGeneratorContext _ctx;

        protected abstract ExpressionSyntax VisitComposableMethodCall(InvocationExpressionSyntax invocationExpression);
        protected abstract ExpressionSyntax VisitComposableArgumentCall(ExpressionSyntax expression, DelegateMethodCallInfo delegateMethodCallInfo);
      
        public override SyntaxNode VisitMethodDeclaration(MethodDeclarationSyntax method)
        {
            var sourceLocationAnnotation = method.CreateLocationSyntaxAnnotation();

            _ctx.MethodParameters = method.GetParametersInfos(_semanticModel);
            var methodModifiers = method.Modifiers;

            bool hasAnyComposables = _ctx.MethodParameters.Any(p => p.IsComposable);

            ParameterListSyntax newParameterList = method.ParameterList;
            if (hasAnyComposables)
            {
                newParameterList = ReplaceAllComposableParameters(method, true);
            }
            newParameterList = AppendComposableContextrelatedParameters(newParameterList, _ctx.ContextVarName, _ctx.ChangedVarName);

            MethodDeclarationSyntax newMethod = method
                .WithParameterList(newParameterList)
                .WithModifiers(methodModifiers)
                .WithAttributeLists(ReplaceComposableAttribute(method.AttributeLists));

            if (method.Body != null)
            {
                BlockSyntax transformedBody = base.Visit(method.Body) as BlockSyntax;
                newMethod = newMethod.WithBody(transformedBody);
            }
            else if (method.ExpressionBody != null)
            {
                throw new NotSupportedException();
            }

            if (sourceLocationAnnotation != null)
                newMethod = newMethod.WithAdditionalAnnotations(sourceLocationAnnotation);

            return newMethod;
        }

        public override SyntaxNode VisitBlock(BlockSyntax node)
        {
            if (node.Parent is MethodDeclarationSyntax)
            {
                return VisitMethodDeclarationBlock(node);
            }
            return base.VisitBlock(node);
        }

        protected SyntaxNode VisitMethodDeclarationBlock(BlockSyntax node)
        {
            string ctxVar = _ctx.ContextVarName;
            string changedVar = _ctx.ChangedVarName;
            using ListPoolObject<StatementSyntax> syntaxList = ListPool<StatementSyntax>.Get();

            syntaxList.Add(SyntaxFactoryHelpers.CreateSafeMethodCallOnVariableWithArgs(
                ctxVar,
                Consts.ComposeContext.StartRestartableGroupMethod,
                SyntaxFactoryHelpers.CreateIntLiteral(_ctx.InitialGroupId)));

            var normalParams = _ctx.MethodParameters
                .Select((p, i) => (Param: p, Index: i))
                .Where(x => !x.Param.IsComposable)
                .ToList();

            bool anyNormalParams = normalParams.Any();
            bool allStable = anyNormalParams
                && normalParams.All(x => x.Param.Type != null && x.Param.Type.IsStableType());
            _ctx.HasUnstableParam = anyNormalParams && !allStable;

            BlockSyntax processedBody = base.VisitBlock(node) as BlockSyntax;

            if (allStable && anyNormalParams)
            {
                var stateVarNames = new List<string>();
                foreach (var (param, index) in normalParams)
                {
                    string stateVar = $"__{param.Name}_state";
                    stateVarNames.Add(stateVar);

                    syntaxList.Add(SyntaxFactory.LocalDeclarationStatement(
                        SyntaxFactory.VariableDeclaration(
                            SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.ByteKeyword)))
                        .WithVariables(
                            SyntaxFactory.SingletonSeparatedList(
                                SyntaxFactory.VariableDeclarator(
                                    SyntaxFactory.Identifier(stateVar))
                                .WithInitializer(SyntaxFactory.EqualsValueClause(
                                    SyntaxFactory.ElementAccessExpression(
                                        SyntaxFactory.IdentifierName(changedVar))
                                    .WithArgumentList(SyntaxFactory.BracketedArgumentList(
                                        SyntaxFactory.SingletonSeparatedList(
                                            SyntaxFactory.Argument(
                                                SyntaxFactoryHelpers.CreateIntLiteral(index))))))))))
                        .WithTrailingNewLine());

                    if (param.Type != null && param.Type.IsStableType())
                    {
                        syntaxList.Add(SyntaxFactory.IfStatement(
                            SyntaxFactory.BinaryExpression(
                                SyntaxKind.EqualsExpression,
                                SyntaxFactory.IdentifierName(stateVar),
                                SyntaxFactory.MemberAccessExpression(
                                    SyntaxKind.SimpleMemberAccessExpression,
                                    SyntaxFactory.ParseTypeName(Consts.ComposableArgumentsState.FullName),
                                    SyntaxFactory.IdentifierName(Consts.ComposableArgumentsState.UncertainField))),
                            SyntaxFactory.Block(
                                SyntaxFactory.SingletonList<StatementSyntax>(
                                    SyntaxFactory.ExpressionStatement(
                                        SyntaxFactory.AssignmentExpression(
                                            SyntaxKind.SimpleAssignmentExpression,
                                            SyntaxFactory.IdentifierName(stateVar),
                                            SyntaxFactory.ConditionalExpression(
                                                SyntaxFactory.InvocationExpression(
                                                    SyntaxFactory.MemberAccessExpression(
                                                        SyntaxKind.SimpleMemberAccessExpression,
                                                        SyntaxFactory.IdentifierName(ctxVar),
                                                        SyntaxFactory.IdentifierName(Consts.ComposeContext.ChangedMethod)))
                                                .WithArgumentList(SyntaxFactory.ArgumentList(
                                                    SyntaxFactory.SingletonSeparatedList(
                                                        SyntaxFactory.Argument(SyntaxFactory.IdentifierName(param.Name))))),
                                                SyntaxFactory.MemberAccessExpression(
                                                    SyntaxKind.SimpleMemberAccessExpression,
                                                    SyntaxFactory.ParseTypeName(Consts.ComposableArgumentsState.FullName),
                                                    SyntaxFactory.IdentifierName(Consts.ComposableArgumentsState.DifferentField)),
                                                SyntaxFactory.MemberAccessExpression(
                                                    SyntaxKind.SimpleMemberAccessExpression,
                                                    SyntaxFactory.ParseTypeName(Consts.ComposableArgumentsState.FullName),
                                                    SyntaxFactory.IdentifierName(Consts.ComposableArgumentsState.SameField)))))
                                        .WithTrailingNewLine()))));
                    }
                }

                ExpressionSyntax? condition = null;
                foreach (var stateVar in stateVarNames)
                {
                    var eqToSame = SyntaxFactory.BinaryExpression(
                        SyntaxKind.EqualsExpression,
                        SyntaxFactory.IdentifierName(stateVar),
                        SyntaxFactory.MemberAccessExpression(
                            SyntaxKind.SimpleMemberAccessExpression,
                            SyntaxFactory.ParseTypeName(Consts.ComposableArgumentsState.FullName),
                            SyntaxFactory.IdentifierName(Consts.ComposableArgumentsState.SameField)));

                    var eqToStatic = SyntaxFactory.BinaryExpression(
                        SyntaxKind.EqualsExpression,
                        SyntaxFactory.IdentifierName(stateVar),
                        SyntaxFactory.MemberAccessExpression(
                            SyntaxKind.SimpleMemberAccessExpression,
                            SyntaxFactory.ParseTypeName(Consts.ComposableArgumentsState.FullName),
                            SyntaxFactory.IdentifierName(Consts.ComposableArgumentsState.StaticField)));

                    var eq = SyntaxFactory.ParenthesizedExpression(
                        SyntaxFactory.BinaryExpression(
                            SyntaxKind.LogicalOrExpression,
                            eqToSame,
                            eqToStatic));

                    condition = condition == null
                        ? eq
                        : SyntaxFactory.BinaryExpression(SyntaxKind.LogicalAndExpression, condition, eq);
                }

                condition = SyntaxFactory.BinaryExpression(
                    SyntaxKind.LogicalAndExpression,
                    condition,
                    SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        SyntaxFactory.IdentifierName(ctxVar),
                        SyntaxFactory.IdentifierName(Consts.ComposeContext.SkippingProperty)));

                syntaxList.Add(SyntaxFactory.IfStatement(
                    condition,
                    SyntaxFactory.Block(
                        SyntaxFactory.SingletonList<StatementSyntax>(
                            SyntaxFactory.ExpressionStatement(
                                SyntaxFactory.InvocationExpression(
                                    SyntaxFactory.MemberAccessExpression(
                                        SyntaxKind.SimpleMemberAccessExpression,
                                        SyntaxFactory.IdentifierName(ctxVar),
                                        SyntaxFactory.IdentifierName(Consts.ComposeContext.SkipToGroupEndMethod))))
                                .WithTrailingNewLine())),
                    SyntaxFactory.ElseClause(
                        SyntaxFactory.Block(processedBody.Statements))));
            }
            else
            {
                syntaxList.AddRange(processedBody.Statements);
            }

            syntaxList.Add(SyntaxFactoryHelpers.CreateSafeMethodCallOnVariableWithArgs(
                ctxVar,
                Consts.ComposeContext.EndRestartableGroupMethod,
                SyntaxFactoryHelpers.CreateIntLiteral(_ctx.InitialGroupId)));

            return node.WithStatements(SyntaxFactory.List(syntaxList));
        }

        public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            IMethodSymbol? methodSymbol = _semanticModel.GetSymbolInfo(node).Symbol as IMethodSymbol;
            if (methodSymbol == null)
                return base.VisitInvocationExpression(node);

            ExpressionSyntax? processedMethodCall = ProcessInvokeMethodExpression(node, methodSymbol);
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
            ExpressionSyntax? processedMethodCall = ProcessInvokeMethodExpression(node, methodSymbol);
            if (processedMethodCall != null)
                return processedMethodCall;
            else
                return base.VisitConditionalAccessExpression(node);
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
                    Consts.ComposeContext.StartReplaceableGroupMethod,
                    SyntaxFactoryHelpers.CreateIntLiteral(elseGroupId))
                    .WithTrailingNewLine();
                ExpressionStatementSyntax elseGroupEndStatement = SyntaxFactoryHelpers.CreateSafeMethodCallOnVariableWithArgs(_ctx.ContextVarName,
                    Consts.ComposeContext.EndReplaceableGroupMethod,
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
                        Consts.ComposeContext.StartReplaceableGroupMethod,
                        SyntaxFactoryHelpers.CreateIntLiteral(ifGroupId))
                    .WithTrailingNewLine();

                ExpressionStatementSyntax ifGroupEndStatement = SyntaxFactoryHelpers.CreateSafeMethodCallOnVariableWithArgs(_ctx.ContextVarName,
                    Consts.ComposeContext.EndReplaceableGroupMethod,
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

        protected static SyntaxNode ReplaceLastMemberAccess(SyntaxNode root, string oldMemberName, string newMemberPath)
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

        protected DelegateMethodCallInfo? GetDelegateMethodCallInfo(ExpressionSyntax expression, IMethodSymbol methodSymbol)
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
        protected virtual ExpressionSyntax? ProcessInvokeMethodExpression(ExpressionSyntax expression, IMethodSymbol methodSymbol)
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
        protected InvocationExpressionSyntax ReplaceWithFullQualifiedName(InvocationExpressionSyntax node)
        {
            var symbolInfo = _semanticModel.GetSymbolInfo(node);
            var symbol = symbolInfo.Symbol;

            if (symbol is IMethodSymbol methodSymbol)
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

            return node;
        }
        protected bool IsComposableAttributeSyntax(AttributeSyntax s)
        {
            var name = s.Name.ToString();
            return name == Consts.ComposableAttributeFullName ||
                    name.EndsWith("Composable") ||
                    name.EndsWith("ComposableAttribute");
        }

        protected SyntaxList<AttributeListSyntax> ReplaceComposableAttribute(SyntaxList<AttributeListSyntax> attributeLists)
        {
            return SyntaxFactory.List(attributeLists.Select(aList =>
            {
                IEnumerable<AttributeSyntax> newAttributes = aList.Attributes.Select(attribute =>
                {
                    if (IsComposableAttributeSyntax(attribute))
                        return SyntaxFactory.Attribute(SyntaxFactory.IdentifierName(Consts.ComposeGeneratedAttributeFullTypeName));
                    else
                        return attribute;
                });
                return aList.WithAttributes(SyntaxFactory.SeparatedList(newAttributes));
            }));
        }

        protected static IEnumerable<StatementSyntax> WrapStatementsWithGroupStartAndEndMethods(ExpressionStatementSyntax groupStart,
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
        protected ParameterListSyntax AppendComposableContextrelatedParameters(ParameterListSyntax paramList, string contextParamName, string changedParamName)
        {
            SeparatedSyntaxList<ParameterSyntax> newArguments = paramList.Parameters.AddRange(new ParameterSyntax[]
                    {
                SyntaxFactory.Parameter(default,
                    default,
                    SyntaxFactory.ParseTypeName(Consts.ComposeContext.FullName).WithTrailingSpace(),
                    SyntaxFactory.Identifier(contextParamName),
                    default),

                SyntaxFactory.Parameter(default,
                    default,
                    SyntaxFactory.ParseTypeName(ComposableArgumentsState.FullName).WithTrailingSpace(),
                    SyntaxFactory.Identifier(changedParamName),
                    default),
                    });

            return paramList.WithParameters(newArguments);
        }

        protected ParameterListSyntax ReplaceAllComposableParameters(MethodDeclarationSyntax method, bool addAttributeToComposableParameters)
        {
            var args = method.ParameterList.Parameters.Zip(
                    method.GetParametersInfos(_semanticModel),
                    (parameter, paramInfo) => (parameter, paramInfo)
            );

            SeparatedSyntaxList<ParameterSyntax> newArguments = SyntaxFactory.SeparatedList(
                args.Select(s => (Syntax: s.parameter, ParamInfo: s.paramInfo))
                        .Select(oldParam =>
                        SyntaxFactory.Parameter(
                            addAttributeToComposableParameters
                                ? (oldParam.ParamInfo.IsComposable
                                    ? ReplaceComposableActionParameterAttributes(oldParam.Syntax.AttributeLists)
                                    : oldParam.Syntax.AttributeLists)
                                : default,
                            oldParam.Syntax.Modifiers,
                            oldParam.ParamInfo.IsComposable
                                ? SyntaxFactory.ParseTypeName(Consts.ComposableAction.FullNameWithGenericArguments(oldParam.ParamInfo.GenericArguments.Select(t => t.GetFullMetadataName()))).WithTrailingSpace()
                                : oldParam.Syntax.Type,
                            oldParam.Syntax.Identifier,
                            ReplaceDefaultArgumentValue(oldParam.Syntax.Default, oldParam.ParamInfo.IsComposable)
            )));

            return SyntaxFactory.ParameterList(newArguments);
        }


        private SyntaxList<AttributeListSyntax> ReplaceComposableActionParameterAttributes(SyntaxList<AttributeListSyntax> attributeLists)
        {
            return SyntaxFactory.List(attributeLists.Select(aList =>
            {
                IEnumerable<AttributeSyntax> newAttributes = aList.Attributes.Select(attribute =>
                {
                    if (MethodDeclarationSyntaxExtensions.IsComposableAttribute(attribute, _semanticModel))
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
        protected record DelegateMethodCallInfo(string RecieverObjectName, bool IsSimpleMemberAccessCall, bool IsDirectCall, bool IsNullSafeCall);
    }
}
