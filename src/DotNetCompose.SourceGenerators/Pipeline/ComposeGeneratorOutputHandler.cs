using DotNetCompose.SourceGenerators.Diagnostics;
using DotNetCompose.SourceGenerators.Emitters;
using DotNetCompose.SourceGenerators.Extensions;
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

namespace DotNetCompose.SourceGenerators.Pipeline
{
    internal sealed class ComposeGeneratorOutputHandler : IOutputHandler
    {
        public void Handle(SourceProductionContext spc, Compilation compilation, ClassAndComposablesMethods input, PipelineContext context)
        {
            string typeName = input.ClassName;
            DiagnosticReporter reporter = new DiagnosticReporter();
            string sourceCode = GenerateComposableMethods(input, compilation, reporter, context);

            foreach (DiagnosticInfo diag in reporter.ToImmutable())
                spc.ReportDiagnostic(diag.ToDiagnostic());

            if (!string.IsNullOrEmpty(sourceCode))
            {
                spc.AddSource($"{typeName.Replace('.', '_')}.DuplicatedMethods.g.cs",
                    SourceText.From(sourceCode, Encoding.UTF8));
            }
        }

        private static string GenerateComposableMethods(
            ClassAndComposablesMethods classAndComposablesMethods,
            Compilation compilation,
            IDiagnosticReporter diagnostics,
            PipelineContext pipelineContext)
        {
            ImmutableArray<MethodFullNameAndDeclaration> typeMethods = classAndComposablesMethods.Methods;
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

                    RewriterOptions options = new RewriterOptions(
                        Consts.Rewriter.ContextParamName,
                        Consts.Rewriter.ChangedParamName,
                        Consts.Rewriter.DefaultParamName,
                        Consts.Rewriter.StoredLambdaClassName,
                        Consts.Rewriter.BuildersClassName);

                    MethodGenerationContext methodCtx = new MethodGenerationContext(
                        methodParams,
                        methodParams.Any(p => p.DefaultProviderType != null),
                        hasUnstable);

                    int initialGroupId = RewriterSession.DeterministicHash(m.GetMethodID(semanticModel));
                    RewriterSession session = new RewriterSession(initialGroupId, diagnostics);

                    return (Options: options, MethodCtx: methodCtx, Session: session, Method: m);
                })
            .Select(pair =>
            {
                SyntaxNode body = ComposeSyntaxRewriter.Rewrite(
                    pair.Options, pair.MethodCtx, pair.Session, semanticModel, pair.Method,
                    pipelineContext.MethodCallHandlers, pipelineContext.WellKnownRegistry,
                    pipelineContext.Strategies);
                return (Session: pair.Session, MethodBody: pair.Session.HasErrors ? null : body);
            })
            .Where(x => x.MethodBody != null)
            .ToImmutableArray();

            if (!rewrittenMethods.Any())
                return string.Empty;

            CodeGenerationInput input = new CodeGenerationInput(
                Namespace: namespaceName,
                TypeName: typeName,
                Accessibility: accessibility,
                Usings: usings,
                BuilderMethods: rewrittenMethods.Select(p => p.MethodBody!).ToImmutableArray(),
                Sessions: rewrittenMethods.Select(p => p.Session).ToImmutableArray());

            var emitter = new DefaultCodeEmitter();
            return emitter.Emit(input);
        }
    }
}
