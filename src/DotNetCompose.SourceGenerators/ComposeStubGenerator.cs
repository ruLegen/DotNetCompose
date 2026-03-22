using DotNetCompose.SourceGenerators.Extensions;
using DotNetCompose.SourceGenerators.Rewriters;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using static DotNetCompose.SourceGenerators.ComposeSourceGenerator;

namespace DotNetCompose.SourceGenerators
{
    internal class ComposeStubGenerator
    {
        public static void ExecuteStubGenerator(Compilation compilation, ClassAndComposablesMethods classAndComposablesMethods, SourceProductionContext context)
        {
            string typeName = classAndComposablesMethods.ClassName;
            string sourceCode = GenerateStubComposablesMethods(classAndComposablesMethods, compilation);

            if (!string.IsNullOrEmpty(sourceCode))
            {
                context.AddSource($"{typeName.Replace('.', '_')}.Stubs.g.cs", SourceText.From(sourceCode, Encoding.UTF8));
            }
        }


        private static string GenerateStubComposablesMethods(ClassAndComposablesMethods classAndComposablesMethods, Compilation compilation)
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

            sourceBuilder.AppendLine($"         public partial class {Consts.Rewriter.BuildersClassName} {{");

            foreach (var method in typeMethods)
            {
                var methodParameters = method.GetParametersInfos(semanticModel);
                var methodModifiers = method.Modifiers;
                if (!methodModifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)))
                    methodModifiers = methodModifiers.Add(SyntaxFactory.Token(SyntaxKind.PartialKeyword));

                bool hasAnyComposables = methodParameters.Any(p => p.IsComposable);

                ParameterListSyntax newParameterList = method.ParameterList;
                if (hasAnyComposables)
                    newParameterList = ComposeMethodRewriter.ReplaceAllComposableParameters(method, semanticModel, false);

                newParameterList = ComposeMethodRewriter.AppendComposableContextrelatedParameters(newParameterList,
                                                                                                  semanticModel,
                                                                                                  Consts.Rewriter.ContextParamName,
                                                                                                  Consts.Rewriter.ChangedParamName);
                sourceBuilder.AppendLine($"{methodModifiers} {method.ReturnType} {method.Identifier.ValueText}{newParameterList};");
            }

            sourceBuilder.AppendLine("          }");
            sourceBuilder.AppendLine("      }");
            sourceBuilder.AppendLine("}");

            return sourceBuilder.ToString();
        }
    }
}
