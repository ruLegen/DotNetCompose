using DotNetCompose.SourceGenerators.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using static DotNetCompose.SourceGenerators.ComposeSourceGenerator;

namespace DotNetCompose.SourceGenerators.Pipeline
{
    internal sealed class ComposeStubOutputHandler : IOutputHandler
    {
        public void Handle(SourceProductionContext spc, Compilation compilation, ClassAndComposablesMethods input, PipelineContext context)
        {
            string typeName = input.ClassName;
            string sourceCode = GenerateStubComposablesMethods(input, compilation);

            if (!string.IsNullOrEmpty(sourceCode))
            {
                spc.AddSource($"{typeName.Replace('.', '_')}.Stubs.g.cs", SourceText.From(sourceCode, Encoding.UTF8));
            }
        }

        private static string GenerateStubComposablesMethods(ClassAndComposablesMethods classAndComposablesMethods, Compilation compilation)
        {
            var typeMethods = classAndComposablesMethods.Methods.Select(m => m.Declaration!);
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

            foreach (MethodDeclarationSyntax method in typeMethods)
            {
                ImmutableArray<MethodDeclarationSyntaxExtensions.MethodParameterInfo> methodParameters = method.GetParametersInfos(semanticModel);
                SyntaxTokenList methodModifiers = method.Modifiers;
                if (!methodModifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)))
                    methodModifiers = methodModifiers.Add(SyntaxFactory.Token(SyntaxKind.PartialKeyword));

                bool hasAnyComposables = methodParameters.Any(p => p.IsComposable);

                ParameterListSyntax newParameterList = method.ParameterList;
                if (hasAnyComposables)
                    newParameterList = ParameterListTransformer.ReplaceAllComposableParameters(method, semanticModel, false);

                newParameterList = ParameterListTransformer.AppendComposableContextrelatedParameters(newParameterList,
                                                                                                    Consts.Rewriter.ContextParamName,
                                                                                                    Consts.Rewriter.ChangedParamName,
                                                                                                    Consts.Rewriter.DefaultParamName);
                sourceBuilder.AppendLine($"{methodModifiers} {method.ReturnType} {method.Identifier.ValueText}{newParameterList};");
            }

            sourceBuilder.AppendLine("          }");
            sourceBuilder.AppendLine("      }");
            sourceBuilder.AppendLine("}");

            return sourceBuilder.ToString();
        }
    }
}
