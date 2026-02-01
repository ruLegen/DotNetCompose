using DotNetCompose.SourceGenerators.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Text;

namespace DotNetCompose.SourceGenerators
{
    [Generator(LanguageNames.CSharp)]
    public partial class ComposeGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
#if DEBUG
            if (!Debugger.IsAttached)
            {
                //Debugger.Launch();
            }
#endif       
            // определяем генерируемый код
            IncrementalValuesProvider<MethodDeclarationSyntax> methodDeclarations = context
                .SyntaxProvider
                .ForAttributeWithMetadataName(Consts.ComposableAttributeFullName,
                    static (node, token) => node is MethodDeclarationSyntax,
                    static (ctx, token) => ctx.TargetNode as MethodDeclarationSyntax)
                .Where(m => m != null);

            var compilationAndMethods = context.CompilationProvider.Combine(methodDeclarations.Collect());

            context.RegisterSourceOutput(compilationAndMethods,
                static (spc, source) => Execute(source.Left, source.Right, spc));
        }

        private static void Execute(Compilation compilation,
            ImmutableArray<MethodDeclarationSyntax> methods,
            SourceProductionContext context)
        {
            IEnumerable<IGrouping<string, MethodDeclarationSyntax>> methodsByType = methods
                .GroupBy(m => m.GetFullTypeName(compilation))
                .Where(g => !string.IsNullOrEmpty(g.Key));



            foreach (IGrouping<string, MethodDeclarationSyntax> typeGroup in methodsByType)
            {
                string typeName = typeGroup.Key;
                string sourceCode = GenerateTypeWithDuplicatedMethods(typeGroup, compilation);

                if (!string.IsNullOrEmpty(sourceCode))
                {
                    context.AddSource($"{typeName.Replace('.', '_')}.DuplicatedMethods.g.cs",
                        SourceText.From(sourceCode, Encoding.UTF8));
                }
            }
        }

        private static string GenerateTypeWithDuplicatedMethods(
            IGrouping<string, MethodDeclarationSyntax> typeMethods,
            Compilation compilation)
        {
            if (!typeMethods.Any())
                return string.Empty;

            MethodDeclarationSyntax firstMethod = typeMethods.First();
            SemanticModel semanticModel = compilation.GetSemanticModel(firstMethod.SyntaxTree);
            IMethodSymbol methodSymbol = semanticModel.GetDeclaredSymbol(firstMethod);
            INamedTypeSymbol containingType = methodSymbol?.ContainingType;

            if (containingType == null)
                return string.Empty;

            string namespaceName = containingType.ContainingNamespace?.ToDisplayString() ?? string.Empty;
            string typeName = containingType.Name;

            StringBuilder sourceBuilder = new StringBuilder();

            // Add using directives from original file
            SyntaxNode root = firstMethod.SyntaxTree.GetRoot();
            IEnumerable<UsingDirectiveSyntax> usings = root.DescendantNodes().OfType<UsingDirectiveSyntax>();
            foreach (UsingDirectiveSyntax usingDirective in usings.Distinct(UsingDerectiveComparerByName.Default))
            {
                sourceBuilder.AppendLine(usingDirective.ToFullString());
            }

            sourceBuilder.AppendLine();
            sourceBuilder.AppendLine($"namespace {namespaceName}");
            sourceBuilder.AppendLine("{");
            sourceBuilder.AppendLine($"{containingType.DeclaredAccessibility.ToString().ToLower()} partial class {typeName}");
            sourceBuilder.AppendLine("    {");

            foreach (MethodDeclarationSyntax method in typeMethods)
            {
                var duplicatedMethod = TransformMethod(method, semanticModel, "Generated");
                sourceBuilder.AppendLine("        " + duplicatedMethod.ToFullString());
            }

            sourceBuilder.AppendLine("    }");
            sourceBuilder.AppendLine("}");
            return sourceBuilder.ToString();
        }

        private static MethodDeclarationSyntax TransformMethod(
             MethodDeclarationSyntax method,
             SemanticModel semanticModel,
             string suffix)
        {
            string newName = string.Format("{0}_{1}", method.Identifier.Text, suffix);
            bool hasAnyComposables = method.HasAnyComposablesParamaters(semanticModel);

            ParameterListSyntax newParameterList = method.ParameterList;
            if (hasAnyComposables)
            {
                newParameterList = ReplaceAllComposableArguments(method, semanticModel);
            }

            MethodDeclarationSyntax newMethod = method
                .WithIdentifier(SyntaxFactory.Identifier(newName))
                .WithParameterList(newParameterList)
                .WithAttributeLists(ReplaceComposableAttribute(method.AttributeLists, semanticModel));

            if (method.Body != null)
            {
                BlockSyntax transformedBody = TransformBlock(method.Identifier.Text, method.Body, semanticModel);
                newMethod = newMethod.WithBody(transformedBody);
            }
            else if (method.ExpressionBody != null)
            {
                throw new NotSupportedException();
            }

            return newMethod;
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

        private static EqualsValueClauseSyntax? ReplaceDefaultArgumentValue(EqualsValueClauseSyntax defaultSyntax, bool isComposable)
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

        private static BlockSyntax TransformBlock(string methodName, BlockSyntax block, SemanticModel semanticModel)
        {
            ComposableMethodGeneratorContext composableContext = new ComposableMethodGeneratorContext();
            string contextVarName = "var0";
            List<StatementSyntax> statements = new List<StatementSyntax>();
            statements.Add(SyntaxFactoryHelpers.CreateStaticCallWithVar(
                    Consts.ComposeScope.FullName,
                    Consts.ComposeScope.GetCurrentContextMethodName,
                   contextVarName));
            statements.Add(SyntaxFactoryHelpers.CreateSafeMethodCallOnVariableWithArgs(
                contextVarName,
                Consts.ComposeContext.StartGroupMethod,
                SyntaxFactoryHelpers.CreateIntLiteral(composableContext.InitialGroupId)));

            foreach (StatementSyntax statement in block.Statements)
            {
                TransformStatement(statement, semanticModel, statements, composableContext);
            }

            statements.Add(SyntaxFactoryHelpers.CreateSafeMethodCallOnVariableWithArgs(
                contextVarName,
                Consts.ComposeContext.EndGroupMethod,
                SyntaxFactoryHelpers.CreateIntLiteral(composableContext.InitialGroupId)));

            return block.WithStatements(SyntaxFactory.List(statements));
        }


        private static void TransformStatement(StatementSyntax statement, SemanticModel semanticModel, IList<StatementSyntax> outStatements, ComposableMethodGeneratorContext composableContext)
        {
            switch (statement)
            {
                case ExpressionStatementSyntax exprStatement:
                    TransformExpressionStatement(exprStatement, semanticModel, outStatements, composableContext);
                    break;

                //IfStatementSyntax ifStatement => TransformIfStatement(ifStatement),
                //SwitchStatementSyntax switchStatement => TransformSwitchStatement(switchStatement),
                //ForStatementSyntax forStatement => TransformForStatement(forStatement),
                //WhileStatementSyntax whileStatement => TransformWhileStatement(whileStatement),
                //ForEachStatementSyntax forEachStatement => TransformWhileStatement(forEachStatement),
                //LocalDeclarationStatementSyntax localDecl => TransformLocalDeclaration(localDecl),
                //ReturnStatementSyntax returnStatement => TransformReturnStatement(returnStatement),
                default:
                    outStatements.Add(statement);
                    break;
            }
            ;
        }

        private static void TransformExpressionStatement(ExpressionStatementSyntax exprStatement, SemanticModel semanticModel, IList<StatementSyntax> outStatements, ComposableMethodGeneratorContext ctx)
        {
            ExpressionSyntax expression = exprStatement.Expression;

            bool isComposableArgumentCall = false;
            switch (expression)
            {
                case InvocationExpressionSyntax invocationExpression:
                    IMethodSymbol? methodSymbol = semanticModel.GetSymbolInfo(invocationExpression).Symbol as IMethodSymbol;
                    if (methodSymbol == null)
                    {
                        writeAndBypass();
                        return;
                    }
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
                            SyntaxFactoryHelpers.CreateIntLiteral(ctx.GetNextGroupId()));
                        outStatements.Add(exprStatement.WithExpression(composeActionCallSyntax.Expression));
                        return;
                    }

                    if (expression is InvocationExpressionSyntax invocation)
                    {
                        bool isComposableFunctionCall = methodSymbol.HasAnyComposablesArguments();
                        if (!isComposableFunctionCall)
                        {
                            outStatements.Add(exprStatement.WithExpression(expression));
                            return;
                        }

                        List<(string Name, bool)> argumentInfos = methodSymbol
                            .Parameters
                            .Select(p => (p.Name, p.Type.IsComposableAction()))
                            .ToList();

                        ArgumentListSyntax newArgs = SyntaxFactory.ArgumentList(
                            SyntaxFactory.SeparatedList<ArgumentSyntax>(
                                invocation.ArgumentList.Arguments.Select((arg, index) =>
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
                        outStatements.Add(exprStatement.WithExpression(invocation.WithArgumentList(newArgs)));
                    }
                    break;
                case ConditionalAccessExpressionSyntax conditionalAccessExpression:
                    IParameterSymbol? symbol = semanticModel.GetSymbolInfo(conditionalAccessExpression.Expression).Symbol as IParameterSymbol;
                    if(symbol == null)
                    {
                        writeAndBypass();
                        return;
                    }
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
                            SyntaxFactoryHelpers.CreateIntLiteral(ctx.GetNextGroupId()));
                        outStatements.Add(exprStatement.WithExpression(composeActionCallSyntax.Expression));
                        return;
                    }
                    break;
                default:
                    writeAndBypass();
                    break;
            }


            void writeAndBypass()
            {
                outStatements.Add(exprStatement.WithExpression(expression));
            }
        }

        private static void TODO(string v)
            => throw new NotImplementedException("TODO: " + v);


    }
}
