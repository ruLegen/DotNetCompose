using DotNetCompose.SourceGenerators.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;

namespace DotNetCompose.SourceGenerators
{
    record ClassAndComposablesMethods(string ClassName, ImmutableArray<MethodDeclarationSyntax> Methods);
    record MethodFullNameAndDeclaration(string FullName, MethodDeclarationSyntax Declaration);

    [Generator(LanguageNames.CSharp)]
    public partial class ComposeSourceGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
#if DEBUG
            if (!Debugger.IsAttached)
            {
                //Debugger.Launch();
            }
#endif       
            IncrementalValuesProvider<MethodFullNameAndDeclaration> composableMethodsDeclarations = context
                .SyntaxProvider
                .ForAttributeWithMetadataName<MethodFullNameAndDeclaration>(Consts.ComposableAttributeFullName,
                    static (node, token) => node is MethodDeclarationSyntax,
                    static (ctx, token) =>
                    {
                        return new(ctx.TargetSymbol.GetFullMetadataName(), ctx.TargetNode as MethodDeclarationSyntax);
                    });

            IncrementalValuesProvider<string> composableIgnoredMethodNames = context
                .SyntaxProvider
                .ForAttributeWithMetadataName(Consts.ComposableIgnoreAttributeFullName,
                    static (node, token) => node is MethodDeclarationSyntax,
                    static (ctx, token) => ctx.TargetSymbol.GetFullMetadataName());

            IncrementalValueProvider<ImmutableArray<MethodDeclarationSyntax>> filteredMethods =
                composableMethodsDeclarations
                    .Collect()
                    .Combine(composableIgnoredMethodNames.Collect())
                    .Select((combined, token) =>
                    {
                        var (methods, ignoredNames) = combined;
                        return methods
                            .Where(m => !ignoredNames.Contains(m.FullName))
                            .Select(m=>m.Declaration)
                            .ToImmutableArray();
                    });

            IncrementalValueProvider<(Compilation Left, ImmutableArray<MethodDeclarationSyntax> Right)> compilationAndMethods
                = context.CompilationProvider.Combine(filteredMethods);

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

            //context.RegisterSourceOutput(executeValueProvider, 
            //    static (spc, source) => ComposeStubGenerator.ExecuteStubGenerator(source.Compilation, source.ClassAndMethods, spc));

            context.RegisterImplementationSourceOutput(executeValueProvider,
                static (spc, source) => ComposeGenerator.ExecuteComposeGenerator(source.Compilation, source.ClassAndMethods, spc));
        }



    }
}
