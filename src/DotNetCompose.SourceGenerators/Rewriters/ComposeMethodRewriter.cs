using DotNetCompose.SourceGenerators.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace DotNetCompose.SourceGenerators.Rewriters
{
    internal class ComposeMethodRewriter : CSharpSyntaxRewriter
    {
        public ComposeMethodRewriter(SemanticModel semanticModel)
        {
            _semanticModel = semanticModel;
            _ctx = new ComposableMethodGeneratorContext();
        }

        private SemanticModel _semanticModel;
        private ComposableMethodGeneratorContext _ctx;
        private string _suffix = "Generated";
        public override SyntaxNode VisitMethodDeclaration(MethodDeclarationSyntax method)
        {
            string newName = string.Format("{0}_{1}", method.Identifier.Text, _suffix);
            bool hasAnyComposables = method.HasAnyComposablesParamaters(_semanticModel);

            ParameterListSyntax newParameterList = method.ParameterList;
            if (hasAnyComposables)
            {
                newParameterList = ReplaceAllComposableArguments(method, _semanticModel);
            }

            MethodDeclarationSyntax newMethod = method
                .WithIdentifier(SyntaxFactory.Identifier(newName))
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

        private SyntaxNode VisitMethodDeclarationBlock(BlockSyntax node)
        {
            string contextVarName = _ctx.ContextVarName;
            using ListPoolObject<StatementSyntax> syntaxList = ListPool<StatementSyntax>.Get();

            syntaxList.Add(SyntaxFactoryHelpers.CreateStaticCallWithVar(
                      Consts.ComposeScope.FullName,
                      Consts.ComposeScope.GetCurrentContextMethodName,
                     contextVarName));

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

        public override SyntaxNode VisitExpressionStatement(ExpressionStatementSyntax exprStatement)
        {
            //return base.VisitExpressionStatement(node);
            ExpressionSyntax expression = exprStatement.Expression;

            bool isComposableArgumentCall = false;
            switch (expression)
            {
                case InvocationExpressionSyntax invocationExpression:
                    IMethodSymbol? methodSymbol = _semanticModel.GetSymbolInfo(invocationExpression).Symbol as IMethodSymbol;
                    if (methodSymbol == null)
                        return exprStatement;

                    // Process composable argument function call
                    // Replace regular ComposableAction call to ComposableLambdaWrapper.Invoke
                    isComposableArgumentCall = methodSymbol.ReceiverType?.GetFullMetadataName() == Consts.ComposableActionFullTypeName;
                    if (isComposableArgumentCall)
                    {
                        string? recieverObjectName = string.Empty;
                        switch (invocationExpression.Expression)
                        {
                            case IdentifierNameSyntax identifierNameSyntax:
                                recieverObjectName = identifierNameSyntax.Identifier.Text;
                                break;
                            case MemberAccessExpressionSyntax memberAccessExpressionSyntax:
                                recieverObjectName = (memberAccessExpressionSyntax.Expression as IdentifierNameSyntax)?.Identifier.Text;
                                break;
                        }

                        if (string.IsNullOrEmpty(recieverObjectName))
                            throw new NotSupportedException();
                        ExpressionStatementSyntax composeActionCallSyntax = SyntaxFactoryHelpers.CreateMethodCallOnVariableWithArgs(
                            recieverObjectName,
                            Consts.ComposableLabmdaWrapper.InvokeMethod,
                            SyntaxFactoryHelpers.CreateIntLiteral(_ctx.GetNextGroupId()));

                        _ctx.ComposableProcessed();
                        return exprStatement.WithExpression(composeActionCallSyntax.Expression); ;
                    }

                    bool isComposableFunctionCall = methodSymbol.HasAnyComposablesArguments();
                    if (!isComposableFunctionCall)
                        return exprStatement;

                    List<(string Name, bool)> argumentInfos = methodSymbol
                        .Parameters
                        .Select(p => (p.Name, p.Type.IsComposableAction()))
                        .ToList();

                    ArgumentListSyntax newArgs = SyntaxFactory.ArgumentList(
                        SyntaxFactory.SeparatedList<ArgumentSyntax>(
                            invocationExpression.ArgumentList.Arguments.Select((arg, index) =>
                            {
                                bool isComposable = false;
                                if (arg.NameColon != null)
                                {
                                    (string Name, bool)? argInfo = argumentInfos.FirstOrDefault(a => a.Name == arg.NameColon.Name.Identifier.ValueText);
                                    isComposable = argInfo?.Item2 ?? false;
                                }
                                else
                                {
                                    isComposable = argumentInfos[index].Item2;
                                }
                                if (!isComposable)
                                    return arg;

                                return SyntaxFactory.Argument(
                                        SyntaxFactory.ParenthesizedExpression(
                                            SyntaxFactory.CastExpression(
                                                SyntaxFactory.ParseTypeName(Consts.ComposableActionFullTypeName),
                                                SyntaxFactory.ParenthesizedExpression(arg.Expression)))
                                        ).WithNameColon(arg.NameColon);
                            })
                        )
                    );
                    _ctx.ComposableProcessed();
                    return exprStatement.WithExpression(invocationExpression.WithArgumentList(newArgs));
                case ConditionalAccessExpressionSyntax conditionalAccessExpression:
                    IParameterSymbol? symbol = _semanticModel.GetSymbolInfo(conditionalAccessExpression.Expression).Symbol as IParameterSymbol;
                    if (symbol == null)
                        return conditionalAccessExpression;

                    // Process composable argument function call
                    // Replace regular ComposableAction call to ComposableLambdaWrapper.Invoke
                    isComposableArgumentCall = symbol.Type?.GetFullMetadataName() == Consts.ComposableActionFullTypeName;
                    if (isComposableArgumentCall)
                    {
                        string? recieverObjectName = string.Empty;
                        switch (conditionalAccessExpression.Expression)
                        {
                            case IdentifierNameSyntax identifierNameSyntax:
                                recieverObjectName = identifierNameSyntax.Identifier.Text;
                                break;
                        }

                        if (string.IsNullOrEmpty(recieverObjectName))
                            throw new NotSupportedException();

                        ExpressionStatementSyntax composeActionCallSyntax = SyntaxFactoryHelpers.CreateMethodCallOnVariableWithArgs(
                            recieverObjectName,
                            Consts.ComposableLabmdaWrapper.InvokeMethod,
                            SyntaxFactoryHelpers.CreateIntLiteral(_ctx.GetNextGroupId()));
                        _ctx.ComposableProcessed();
                        return exprStatement.WithExpression(composeActionCallSyntax.Expression);
                    }
                    else
                        return exprStatement;
                default:
                    return exprStatement;
            }
        }
        public override SyntaxNode VisitIfStatement(IfStatementSyntax node)
        {
            using var ifProcessingHanler = _ctx.WithIfProcessing();
            IEnumerable<StatementSyntax> ifStatementsToProcesss = null;
            if (node.Statement is BlockSyntax blockSyntax)
            {
                ifStatementsToProcesss = blockSyntax.Statements;
            }
            else if (node.Statement is ExpressionStatementSyntax expressionStatementSyntax)
            {
                ifStatementsToProcesss = new StatementSyntax[] { expressionStatementSyntax };
            }
            if (ifStatementsToProcesss != null)
            {
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

                if (_ctx.WasGeneratedComposableFunctionWithinConditionalBlocks)
                {
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
                else
                {
                    return base.VisitIfStatement(node);
                }
            }
            else
            {
                throw new NotSupportedException();
            }
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


        private static ParameterListSyntax ReplaceAllComposableArguments(MethodDeclarationSyntax method, SemanticModel semanticModel)
        {
            return SyntaxFactory.ParameterList(
                SyntaxFactory.SeparatedList(method.ParameterList.Parameters.Select(p => (p, semanticModel.GetSymbolInfo(p.Type).Symbol))
                .Where(s => s.Symbol != null)
                .Select(s => (Syntax: s.p, IsComposable: s.Symbol.GetFullMetadataName() == Consts.ComposableActionFullTypeName))
                .Select(oldParam =>
                    SyntaxFactory.Parameter(oldParam.Syntax.AttributeLists,
                    oldParam.Syntax.Modifiers,
                    oldParam.IsComposable
                        ? SyntaxFactory.ParseTypeName(Consts.NameWithWhiteSpace(Consts.ComposableLabmdaWrapper.FullName))
                        : oldParam.Syntax.Type,
                    oldParam.Syntax.Identifier,
                    ReplaceDefaultArgumentValue(oldParam.Syntax.Default, oldParam.IsComposable)
            ))));
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
    }
}
