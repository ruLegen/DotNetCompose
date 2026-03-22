using DotNetCompose.SourceGenerators.Extensions;
using DotNetCompose.SourceGenerators.Rewriters;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using static DotNetCompose.SourceGenerators.ComposeSourceGenerator;

namespace DotNetCompose.SourceGenerators
{
    internal class ComposeGenerator
    {
        public static void ExecuteComposeGenerator(Compilation compilation,
            ClassAndComposablesMethods classAndComposablesMethods,
            SourceProductionContext context)
        {
            string typeName = classAndComposablesMethods.ClassName;
            string sourceCode = GenerateComposableMethods(classAndComposablesMethods, compilation);

            if (!string.IsNullOrEmpty(sourceCode))
            {
                context.AddSource($"{typeName.Replace('.', '_')}.DuplicatedMethods.g.cs",
                    SourceText.From(sourceCode, Encoding.UTF8));
            }
        }

        private static string GenerateComposableMethods(
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

            // Use a StringWriter as the underlying writer
            using StringWriter writer = new StringWriter(new StringBuilder(), CultureInfo.InvariantCulture);
            using IndentedTextWriter indentWriter = new IndentedTextWriter(writer, Consts.DefaultIndent);
            IndentedTextWriter sourceBuilder = new IndentedTextWriter(writer);

            // Add using directives from original file
            SyntaxNode root = firstMethod.SyntaxTree.GetRoot();
            IEnumerable<UsingDirectiveSyntax> usings = root.DescendantNodes().OfType<UsingDirectiveSyntax>();
            foreach (UsingDirectiveSyntax usingDirective in usings.Distinct(UsingDerectiveComparerByName.Default))
            {
                sourceBuilder.AppendLine(usingDirective.WithoutTrivia().ToFullString());
            }

            sourceBuilder.AppendLine();
            sourceBuilder.AppendLine($"namespace {namespaceName}");
            sourceBuilder.AppendLine("{");
            sourceBuilder.WithIndent(() =>
            {
                sourceBuilder.AppendLine($"{containingType.DeclaredAccessibility.ToString().ToLower()} partial class {typeName}");
                sourceBuilder.AppendLine("{");

                var rewrittenMethods = typeMethods.Select(m => (Method: m,
                                        Context: new ComposableMethodGeneratorContext(Consts.Rewriter.ContextParamName,
                                                     Consts.Rewriter.ChangedParamName,
                                                     Consts.Rewriter.StoredLambdaClassName,
                                                     Consts.Rewriter.BuildersClassName)))
                                       .Select(pair => (Context: pair.Context, MethodBody: ComposeMethodRewriter.Rewrite(pair.Context, semanticModel, pair.Method)))
                                       .ToImmutableArray();

                sourceBuilder.WithIndent(() =>
                {
                    sourceBuilder.AppendLine($"public partial class {Consts.Rewriter.BuildersClassName}");
                    sourceBuilder.AppendLine("{");
                    sourceBuilder.WithIndent(() =>
                    {
                        int currentIndent = sourceBuilder.Indent;
                        foreach (MethodDeclarationSyntax method in rewrittenMethods.Select(pair => pair.MethodBody))
                        {
                            //sourceBuilder.AppendLine("        " + method.NormalizeWhitespace().ToFullString());
                            MethodDeclarationSyntax normalizedMethod = SyntaxNormalizer.Normalize(method, false, currentIndent, Consts.DefaultIndent, Consts.DefaultEOL);
                            sourceBuilder.AppendLineRaw(normalizedMethod.ToFullString());
                        }

                        sourceBuilder.AppendLine($"static class {Consts.Rewriter.StoredLambdaClassName}");
                        sourceBuilder.AppendLine("{");
                        sourceBuilder.WithIndent(() =>
                        {
                            int currentIndent = sourceBuilder.Indent;
                            foreach (ComposableMethodGeneratorContext ctx in rewrittenMethods.Select(pair => pair.Context))
                            {
                                foreach (var storedLambda in ctx.StoredLambdas)
                                {
                                    //var rewrittenLines = new DebugLineNumberSyntaxTreeWriter().Visit(storedLambda.MethodDeclaration);
                                    var normalizedMethod = SyntaxNormalizer.Normalize(storedLambda.MethodDeclaration, false, currentIndent, Consts.DefaultIndent, Consts.DefaultEOL);
                                    sourceBuilder.AppendLineRaw(normalizedMethod.ToFullString());
                                }
                            }
                        });
                        sourceBuilder.AppendLine("}");
                    });
                    sourceBuilder.AppendLine("}");
                });
                sourceBuilder.AppendLine("}");
            });
            sourceBuilder.AppendLine("}");
            return sourceBuilder.InnerWriter.ToString();
        }
    }
}
