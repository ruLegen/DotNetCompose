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
                        .Select(static g => new ClassAndComposablesMethods(g.Key, g.ToImmutableArray()));

                    return methodsByType.ToImmutableArray();
                });


            IncrementalValuesProvider<(ClassAndComposablesMethods ClassAndMethods, Compilation Compilation)> executeValueProvider
                = classAndComposablesMethods.Combine(context.CompilationProvider);

            //context.RegisterSourceOutput(executeValueProvider, 
            //    static (spc, source) => ComposeStubGenerator.ExecuteStubGenerator(source.Compilation, source.ClassAndMethods, spc));

            context.RegisterImplementationSourceOutput(executeValueProvider,
                static (spc, source) => ComposeGenerator.ExecuteComposeGenerator(source.Compilation, source.ClassAndMethods, spc));
        }

        private static int ComputeContentHash(MethodDeclarationSyntax? method)
        {
            if (method == null) return 0;
            int hash = 0;
            foreach (var token in method.DescendantTokens())
            {
                foreach (var c in token.Text)
                    hash = unchecked(hash * 31 + c);
            }
            return hash;
        }
    }
}
