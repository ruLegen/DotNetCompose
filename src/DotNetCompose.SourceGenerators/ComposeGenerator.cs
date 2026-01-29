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
                Debugger.Launch();
            }
#endif             // определяем генерируемый код
            IncrementalValuesProvider<MethodDeclarationSyntax> methodDeclarations = context
                .SyntaxProvider
                .ForAttributeWithMetadataName("DotNetCompose.Runtime.ComposableAttribute",
                    static (node, token) => node is MethodDeclarationSyntax,
                    static (ctx, token) => ctx.TargetNode as MethodDeclarationSyntax)
                .Where(m => m != null);

            var compilationAndMethods = context.CompilationProvider.Combine(methodDeclarations.Collect());

            // Step 3: Generate source code
            context.RegisterSourceOutput(compilationAndMethods,
                static (spc, source) => Execute(source.Left, source.Right, spc));
        }

        private static void Execute(Compilation compilation,
            ImmutableArray<MethodDeclarationSyntax> methods,
            SourceProductionContext context)
        {
            // Group methods by containing type
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
            sourceBuilder.AppendLine($"    partial class {typeName}");
            sourceBuilder.AppendLine("    {");

            foreach (MethodDeclarationSyntax method in typeMethods)
            {
                var duplicatedMethod = TransformMethod(method, semanticModel, "Generated");
                sourceBuilder.AppendLine("        " + duplicatedMethod.ToFullString());
            }


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
                .WithParameterList(newParameterList);

            if (method.Body != null)
            {
                BlockSyntax transformedBody = TransformBlock(method.Body, semanticModel);
                newMethod = newMethod.WithBody(transformedBody);
            }
            else if (method.ExpressionBody != null)
            {
                TODO("method.ExpressionBody");
                //var transformedExpression = TransformExpression(method.ExpressionBody.Expression, transformations);
                //newMethod = newMethod.WithExpressionBody(
                //    SyntaxFactory.ArrowExpressionClause(transformedExpression));
            }

            return newMethod;
        }

        private static ParameterListSyntax ReplaceAllComposableArguments(MethodDeclarationSyntax method, SemanticModel semanticModel)
        {
            return SyntaxFactory.ParameterList(
                SyntaxFactory.SeparatedList(method.ParameterList.Parameters.Select(p => (p, semanticModel.GetSymbolInfo(p.Type).Symbol))
                .Where(s => s.Symbol != null)
                .Select(oldParam =>
                    SyntaxFactory.Parameter(oldParam.p.AttributeLists,
                    oldParam.p.Modifiers,
                    oldParam.Symbol.GetFullMetadataName() == Consts.ComposableActionFullTypeName
                        ? SyntaxFactory.ParseTypeName(Consts.NameWithWhiteSpace(Consts.ComposableLambdaFullTypeName))
                        : oldParam.p.Type,
                    oldParam.p.Identifier,
                    oldParam.p.Default)
            )));
        }

        private static BlockSyntax TransformBlock(BlockSyntax block, SemanticModel semanticModel)
        {
            List<StatementSyntax> statements = new List<StatementSyntax>();

            foreach (StatementSyntax statement in block.Statements)
            {
                statements.AddRange(TransformStatement(statement, semanticModel));
            }

            return block.WithStatements(SyntaxFactory.List(statements));
        }
        private static IList<StatementSyntax> TransformStatement(StatementSyntax statement, SemanticModel semanticModel)
        {
            return statement switch
            {
                ExpressionStatementSyntax exprStatement => TransformExpressionStatement(exprStatement, semanticModel),
                //IfStatementSyntax ifStatement => TransformIfStatement(ifStatement),
                //SwitchStatementSyntax switchStatement => TransformSwitchStatement(switchStatement),
                //ForStatementSyntax forStatement => TransformForStatement(forStatement),
                //WhileStatementSyntax whileStatement => TransformWhileStatement(whileStatement),
                //ForEachStatementSyntax forEachStatement => TransformWhileStatement(forEachStatement),
                //LocalDeclarationStatementSyntax localDecl => TransformLocalDeclaration(localDecl),
                //ReturnStatementSyntax returnStatement => TransformReturnStatement(returnStatement),
                _ => new StatementSyntax[] { statement }
            };
        }

        private static IList<StatementSyntax> TransformExpressionStatement(ExpressionStatementSyntax exprStatement, SemanticModel semanticModel)
        {
            ExpressionSyntax expression = exprStatement.Expression;

            if (expression is InvocationExpressionSyntax invocation)
            {
                IMethodSymbol? methodSymbol = semanticModel.GetSymbolInfo(expression).Symbol as IMethodSymbol;
                if (methodSymbol == null)
                    return new StatementSyntax[] { exprStatement.WithExpression(expression) };

                bool hasAnyComposablesArgs = methodSymbol.HasAnyComposablesArguments();
                if (!hasAnyComposablesArgs)
                    return new StatementSyntax[] { exprStatement.WithExpression(expression) };

                List<StatementSyntax> resultStatements = new List<StatementSyntax>();

                foreach (IParameterSymbol arg in methodSymbol.Parameters)
                {
                    bool isComposableAction = arg.IsComposableAction();
                    if (!isComposableAction)
                        continue;
                    string guid = Guid.NewGuid().ToString("g");
                    StatementSyntax statementSyntax = TypeSyntaxFactory.CreateVariable(
                        Consts.ComposableLambdaFullTypeName, "labmdaWrapper" + guid);
                }

                SeparatedSyntaxList<ArgumentSyntax> args = invocation.ArgumentList.Arguments;
                foreach (ArgumentSyntax arg in args)
                {
                    var symbol = semanticModel.GetDeclaredSymbol(arg.Expression);
                    if (symbol != null)
                    {
                    }
                }
            }

            return new StatementSyntax[] { exprStatement.WithExpression(expression) };
        }

        private static void TODO(string v)
            => throw new NotImplementedException("TODO: " + v);


    }
}
