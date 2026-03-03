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
            // определяем генерируемый код
            IncrementalValuesProvider<MethodDeclarationSyntax> methodDeclarations = context
                .SyntaxProvider
                .ForAttributeWithMetadataName(Consts.ComposableAttributeFullName,
                    static (node, token) => node is MethodDeclarationSyntax,
                    static (ctx, token) => ctx.TargetNode as MethodDeclarationSyntax)
                .Where(m => m != null);

            var compilationAndMethods = context.CompilationProvider.Combine(methodDeclarations.Collect());

            context.RegisterImplementationSourceOutput(compilationAndMethods,
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
                var newMethod = new ComposeMethodRewriter(semanticModel).Visit(method);
                sourceBuilder.AppendLine("        " + newMethod.NormalizeWhitespace().ToFullString());
            }

            sourceBuilder.AppendLine("    }");
            sourceBuilder.AppendLine("}");
            return sourceBuilder.ToString();
        }
    }
}
