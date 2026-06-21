using DotNetCompose.SourceGenerators.Emitters;
using DotNetCompose.SourceGenerators.Extensions;
using DotNetCompose.SourceGenerators.Handlers;
using DotNetCompose.SourceGenerators.Handlers.WellKnown;
using DotNetCompose.SourceGenerators.Rewriters;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using static DotNetCompose.SourceGenerators.ComposeSourceGenerator;

namespace DotNetCompose.SourceGenerators
{
    internal class ComposeGenerator
    {
        private static readonly IReadOnlyList<IMethodCallHandler> DefaultHandlerChain = new IMethodCallHandler[]
        {
            new ComposableMethodCallHandler(),
            new DelegateMethodCallHandler(),
        };

        private static readonly WellKnownFunctionRegistry WellKnownRegistry = WellKnownFunctionRegistry.Empty;

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

            MethodDeclarationSyntax firstMethod = typeMethods.First().Declaration!;
            SemanticModel semanticModel = compilation.GetSemanticModel(firstMethod.SyntaxTree);
            IMethodSymbol methodSymbol = semanticModel.GetDeclaredSymbol(firstMethod);
            INamedTypeSymbol containingType = methodSymbol?.ContainingType;

            if (containingType == null)
                return string.Empty;

            string namespaceName = containingType.ContainingNamespace?.ToDisplayString() ?? string.Empty;
            string typeName = containingType.Name;
            string accessibility = containingType.DeclaredAccessibility.ToString().ToLower();

            SyntaxNode root = firstMethod.SyntaxTree.GetRoot();
            ImmutableArray<UsingDirectiveSyntax> usings = root.DescendantNodes()
                .OfType<UsingDirectiveSyntax>()
                .Distinct(UsingDerectiveComparerByName.Default)
                .ToImmutableArray();

            var rewrittenMethods = typeMethods.Select(m => m.Declaration!)
            .Select(m =>
            {
                var methodParams = m.GetParametersInfos(semanticModel);
                var normalParams = methodParams
                    .Select((p, i) => (Param: p, Index: i))
                    .Where(x => !x.Param.IsComposable)
                    .ToList();
                bool anyNormalParams = normalParams.Any();
                bool allStable = anyNormalParams
                    && normalParams.All(x => x.Param.Type != null && x.Param.Type.IsStableType());
                bool hasUnstable = anyNormalParams && !allStable;

                var options = new RewriterOptions(
                    Consts.Rewriter.ContextParamName,
                    Consts.Rewriter.ChangedParamName,
                    Consts.Rewriter.DefaultParamName,
                    Consts.Rewriter.StoredLambdaClassName,
                    Consts.Rewriter.BuildersClassName);
                var methodCtx = new MethodGenerationContext(
                    methodParams,
                    methodParams.Any(p => p.DefaultProviderType != null),
                    hasUnstable);
                var session = new RewriterSession(RewriterSession.DeterministicHash(m.GetMethodID(semanticModel)));

                return (Options: options, MethodCtx: methodCtx, Session: session, Method: m);
            })
            .Select(pair => (Session: pair.Session, MethodBody: ComposeMethodRewriter.Rewrite(pair.Options, pair.MethodCtx, pair.Session, semanticModel, pair.Method, DefaultHandlerChain, WellKnownRegistry)))
            .ToImmutableArray();

            var input = new CodeGenerationInput(
                Namespace: namespaceName,
                TypeName: typeName,
                Accessibility: accessibility,
                Usings: usings,
                BuilderMethods: rewrittenMethods.Select(p => p.MethodBody).ToImmutableArray(),
                Sessions: rewrittenMethods.Select(p => p.Session).ToImmutableArray());

            var emitter = new DefaultCodeEmitter();
            return emitter.Emit(input);
        }
    }
}
