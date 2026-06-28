using DotNetCompose.SourceGenerators.Diagnostics;
using DotNetCompose.SourceGenerators.Extensions;
using DotNetCompose.SourceGenerators.Pipeline;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;

namespace DotNetCompose.SourceGenerators
{

    [Generator(LanguageNames.CSharp)]
    public partial class ComposeSourceGenerator : IIncrementalGenerator
    {
        private static readonly IComposePipeline _pipeline = new ComposePipelineBuilder()
            .SetStrategies(StrategyContainer.Default) 
            .AddMethodCallHandler<Handlers.MovableContentInvokeHandler>()
            .AddMethodCallHandler<Handlers.ComposableMethodCallHandler>()
            .AddMethodCallHandler<Handlers.DelegateMethodCallHandler>()
            //.AddWellKnownHandler<Handlers.WellKnown.CurrentContextHandler>()
            .AddOutput(new ComposeGeneratorOutputHandler())
            .Build();

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
#if DEBUG
            if (!Debugger.IsAttached)
            {
                // Debugger.Launch();
            }
#endif
            IncrementalValuesProvider<MethodFullNameAndDeclaration> composableMethodsDeclarations = context
                .SyntaxProvider
                .ForAttributeWithMetadataName<MethodFullNameAndDeclaration>(Consts.ComposableAttributeFullName,
                    static (node, token) => node is MethodDeclarationSyntax,
                    static (ctx, token) =>
                    {
                        MethodDeclarationSyntax decl = (MethodDeclarationSyntax)ctx.TargetNode;
                        return new(ctx.TargetSymbol.GetFullMetadataName(), decl, ComputeContentHash(decl));
                    });

            IncrementalValuesProvider<string> composableIgnoredMethodNames = context
                .SyntaxProvider
                .ForAttributeWithMetadataName(Consts.ComposableIgnoreAttributeFullName,
                    static (node, token) => node is MethodDeclarationSyntax,
                    static (ctx, token) => ctx.TargetSymbol.GetFullMetadataName());

            IncrementalValueProvider<ImmutableArray<MethodFullNameAndDeclaration>> filteredMethods =
                composableMethodsDeclarations
                    .Collect()
                    .Combine(composableIgnoredMethodNames.Collect())
                    .Select((combined, token) =>
                    {
                        var (methods, ignoredNames) = combined;
                        return methods
                            .Where(m => !ignoredNames.Contains(m.FullName))
                            .ToImmutableArray();
                    });

            IncrementalValueProvider<(Compilation Left, ImmutableArray<MethodFullNameAndDeclaration> Right)> compilationAndMethods
                = context.CompilationProvider.Combine(filteredMethods);

            IncrementalValuesProvider<ClassAndComposablesMethods> classAndComposablesMethods = compilationAndMethods.SelectMany(
                static (tuple, token) =>
                {
                    (Compilation compilation, ImmutableArray<MethodFullNameAndDeclaration> methods) = tuple;

                    IEnumerable<ClassAndComposablesMethods> methodsByType = methods
                        .GroupBy(m => m.Declaration!.GetFullTypeName(compilation))
                        .Where(static g => !string.IsNullOrEmpty(g.Key))
                        .Select(static g =>
                        {
                            var firstDecl = g.First().Declaration;
                            var classDecl = firstDecl?.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault();
                            bool isPartial = classDecl?.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)) ?? false;
                            return new ClassAndComposablesMethods(g.Key, g.ToImmutableArray(), isPartial);
                        });

                    return methodsByType.ToImmutableArray();
                });

            IncrementalValuesProvider<ValidationResult> validationResults = classAndComposablesMethods
                .SelectMany(static (cls, _) =>
                {
                    var results = new List<ValidationResult>();

                    if (!cls.IsPartial)
                    {
                        var location = cls.Methods.FirstOrDefault()?.Declaration?.GetLocation();
                        results.Add(new ClassResult(cls, new DiagnosticInfo(
                            DiagnosticDescriptors.DNC010_ClassNotPartial,
                            LocationInfo.FromLocation(location),
                            new object[] { cls.ClassName })));
                    }

                    foreach (MethodFullNameAndDeclaration method in cls.Methods)
                    {
                        if (method.Declaration?.ExpressionBody != null)
                        {
                            string methodName = method.Declaration.Identifier.Text;
                            results.Add(new MethodResult(method, new DiagnosticInfo(
                                DiagnosticDescriptors.DNC001_ExpressionBodiedNotSupported,
                                LocationInfo.FromLocation(method.Declaration.ExpressionBody.GetLocation()),
                                new object[] { methodName })));
                        }
                    }

                    if (results.Count == 0)
                    {
                        results.Add(new ClassResult(cls, null));
                    }

                    return results.ToImmutableArray();
                });

            // Class-level early diagnostics
            context.RegisterSourceOutput(
                validationResults.Where(static r => r is ClassResult { Diagnostic: not null })
                                 .Select(static (r, _) => ((ClassResult)r).Diagnostic!.ToDiagnostic()),
                static (spc, d) => spc.ReportDiagnostic(d)
            );

            // Method-level early diagnostics
            context.RegisterSourceOutput(
                validationResults.Where(static r => r is MethodResult { Diagnostic: not null })
                                 .Select(static (r, _) => ((MethodResult)r).Diagnostic!.ToDiagnostic()),
                static (spc, d) => spc.ReportDiagnostic(d)
            );

            // Code generation (valid classes only)
            context.RegisterImplementationSourceOutput(
                validationResults.Where(static r => r is ClassResult { IsValid: true })
                                 .Select(static (r, _) => ((ClassResult)r).Class)
                                 .Combine(context.CompilationProvider),
                static (spc, source) => _pipeline.Execute(spc, source.Right, source.Left)
            );
        }

        private static int ComputeContentHash(MethodDeclarationSyntax method)
        {
            int hash = 0;
            foreach (var node in method.DescendantNodes(descendIntoTrivia: false))
            {
                hash = unchecked(hash * 31 + (int)node.RawKind);
                if (node is IdentifierNameSyntax id)
                {
                    foreach (char c in id.Identifier.ValueText)
                        hash = unchecked(hash * 31 + c);
                }
                else if (node is LiteralExpressionSyntax lit)
                {
                    foreach (char c in lit.Token.ValueText)
                        hash = unchecked(hash * 31 + c);
                }
            }
            return hash;
        }
    }
}
