using DotNetCompose.SourceGenerators.Extensions;
using DotNetCompose.SourceGenerators.Rewriters;
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
    record ClassAndComposablesMethods(string ClassName, ImmutableArray<MethodDeclarationSyntax> Methods);

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
#endif       

            IncrementalValuesProvider<MethodDeclarationSyntax> methodDeclarations = context
                .SyntaxProvider
                .ForAttributeWithMetadataName(Consts.ComposableAttributeFullName,
                    static (node, token) => node is MethodDeclarationSyntax,
                    static (ctx, token) => ctx.TargetNode as MethodDeclarationSyntax)
                .Where(m => m != null);

            IncrementalValueProvider<(Compilation Left, ImmutableArray<MethodDeclarationSyntax> Right)> compilationAndMethods
                = context.CompilationProvider.Combine(methodDeclarations.Collect());

            IncrementalValuesProvider<ClassAndComposablesMethods> classAndComposablesMethods = compilationAndMethods.SelectMany(
                static (tuple, token) =>
                {
                    (Compilation compilation, ImmutableArray<MethodDeclarationSyntax> methods) = tuple;

                    IEnumerable<ClassAndComposablesMethods> methodsByType = methods
                        .GroupBy(m => m.GetFullTypeName(compilation))
                        .Where(static g => !string.IsNullOrEmpty(g.Key))
                        .Select(static g => new ClassAndComposablesMethods(g.Key, g.ToImmutableArray()));

                    return methodsByType.ToImmutableArray();
                });


            IncrementalValuesProvider<(ClassAndComposablesMethods ClassAndMethods, Compilation Compilation)> executeValueProvider
                = classAndComposablesMethods.Combine(context.CompilationProvider);
            //context.RegisterImplementationSourceOutput(compilationAndMethods,
            //    static (spc, source) => Execute(source.Left, source.Right, spc));

            context.RegisterSourceOutput(executeValueProvider,
              static (spc, source) => Execute(source.Compilation, source.ClassAndMethods, spc));
        }

        private static void Execute(Compilation compilation,
            ClassAndComposablesMethods classAndComposablesMethods,
            SourceProductionContext context)
        {
            string typeName = classAndComposablesMethods.ClassName;
            string sourceCode = GenerateTypeWithDuplicatedMethods(classAndComposablesMethods, compilation);

            if (!string.IsNullOrEmpty(sourceCode))
            {
                context.AddSource($"{typeName.Replace('.', '_')}.DuplicatedMethods.g.cs",
                    SourceText.From(sourceCode, Encoding.UTF8));
            }
        }

        private static string GenerateTypeWithDuplicatedMethods(
            ClassAndComposablesMethods classAndComposablesMethods,
            Compilation compilation)
        {
            var typeMethods = classAndComposablesMethods.Methods;
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


            var rewrittenMethods = typeMethods.Select(m => (Method: m,
                                    Context: new ComposableMethodGeneratorContext(Consts.Rewriter.ContextParamName,
                                                 Consts.Rewriter.ChangedParamName,
                                                 Consts.Rewriter.StoredLambdaClassName,
                                                 Consts.Rewriter.BuildersClassName)))
                                   .Select(pair => (Context: pair.Context, MethodBody: ComposeMethodRewriter.Rewrite(pair.Context, semanticModel, pair.Method)))
                                   .ToImmutableArray();

            sourceBuilder.AppendLine($"         public partial class {Consts.Rewriter.BuildersClassName} {{");
            foreach (MethodDeclarationSyntax method in rewrittenMethods.Select(pair => pair.MethodBody))
            {
                var rewrittenLines = new DebugLineNumberSyntaxTreeWriter().VisitMethodDeclaration(method);
                sourceBuilder.AppendLine("        " + rewrittenLines.NormalizeWhitespace().ToFullString());
            }

            sourceBuilder.AppendLine($"             static class {Consts.Rewriter.StoredLambdaClassName} {{");
            foreach (ComposableMethodGeneratorContext ctx in rewrittenMethods.Select(pair => pair.Context))
            {
                foreach (var storedLambda in ctx.StoredLambdas)
                {
                    var rewrittenLines = new DebugLineNumberSyntaxTreeWriter().Visit(storedLambda.MethodDeclaration);
                    sourceBuilder.AppendLine("           " + rewrittenLines.NormalizeWhitespace().ToFullString());
                }
            }
            sourceBuilder.AppendLine("               }");

            sourceBuilder.AppendLine("           }");


            sourceBuilder.AppendLine("      }");
            sourceBuilder.AppendLine("}");
            return sourceBuilder.ToString();
        }
    }
}
