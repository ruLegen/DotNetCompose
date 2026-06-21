using DotNetCompose.SourceGenerators.Diagnostics;
using DotNetCompose.SourceGenerators.Extensions;
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
                        var decl = ctx.TargetNode as MethodDeclarationSyntax;
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
                static (spc, source) => ComposeGenerator.ExecuteComposeGenerator(source.Right, source.Left, spc)
            );
        }

        private static int ComputeContentHash(MethodDeclarationSyntax? method)
        {
            if (method == null) return 0;
            int hash = 0;
            foreach (SyntaxToken token in method.DescendantTokens())
            {
                foreach (char c in token.Text)
                    hash = unchecked(hash * 31 + c);
            }
            return hash;
        }
    }
}
