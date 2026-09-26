using DotNetCompose.SourceGenerators.Diagnostics;
using DotNetCompose.SourceGenerators.Extensions;
using DotNetCompose.SourceGenerators.Helpers;
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
            .AddWellKnownHandler<Handlers.WellKnown.CurrentContextHandler>()
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
                .Combine(context.CompilationProvider)
                .SelectMany(static (source, token) =>
                {
                    ClassAndComposablesMethods cls = source.Left;
                    Compilation compilation = source.Right;
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
                        MethodDeclarationSyntax? declaration = method.Declaration;
                        if (declaration == null) continue;
                        string methodName = declaration.Identifier.Text;
                        if (declaration.ExpressionBody != null)
                        {
                            results.Add(new MethodResult(method, new DiagnosticInfo(
                                DiagnosticDescriptors.DNC001_ExpressionBodiedNotSupported,
                                LocationInfo.FromLocation(declaration.ExpressionBody.GetLocation()),
                                new object[] { methodName })));
                        }
                        SemanticModel semanticModel = compilation.GetSemanticModel(declaration.SyntaxTree);
                        IMethodSymbol? methodSymbol = semanticModel.GetDeclaredSymbol(declaration, token);
                        if (methodSymbol == null)
                            continue;

                        ComposableModeKind methodMode = methodSymbol.GetComposableMode();
                        if (!methodMode.IsValidMethodMode())
                        {
                            results.Add(MethodDiagnostic(
                                method,
                                DiagnosticDescriptors.DNC020_InvalidComposableMode,
                                declaration.Identifier.GetLocation(),
                                methodMode,
                                "method",
                                methodName));
                        }
                        if (methodMode == ComposableModeKind.Inline &&
                            (methodSymbol.IsVirtual || methodSymbol.IsOverride || methodSymbol.IsAbstract || methodSymbol.IsExtern))
                        {
                            results.Add(MethodDiagnostic(
                                method,
                                DiagnosticDescriptors.DNC022_InvalidInlineComposableMethod,
                                declaration.Identifier.GetLocation(),
                                methodName));
                        }
                        if (methodSymbol.OverriddenMethod is { } overriddenMethod &&
                            !HasMatchingComposableContract(methodSymbol, overriddenMethod))
                        {
                            results.Add(MethodDiagnostic(
                                method,
                                DiagnosticDescriptors.DNC023_ComposableOverrideModeMismatch,
                                declaration.Identifier.GetLocation(),
                                methodName));
                        }
                        if (methodSymbol is { IsStatic: false } && HasGeneratedSignatureConflict(methodSymbol))
                        {
                            results.Add(MethodDiagnostic(
                                method,
                                DiagnosticDescriptors.DNC018_GeneratedSignatureConflict,
                                declaration.Identifier.GetLocation(),
                                methodName));
                        }
                        if (declaration.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.AsyncKeyword)))
                        {
                            results.Add(MethodDiagnostic(
                                method,
                                DiagnosticDescriptors.DNC012_AsyncComposable,
                                declaration.Identifier.GetLocation(),
                                methodName));
                        }
                        if (declaration.DescendantNodes().Any(node => node is YieldStatementSyntax))
                        {
                            results.Add(MethodDiagnostic(
                                method,
                                DiagnosticDescriptors.DNC013_IteratorComposable,
                                declaration.Identifier.GetLocation(),
                                methodName));
                        }
                        if (declaration.ParameterList.Parameters.Any(parameter =>
                            parameter.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.RefKeyword) ||
                                modifier.IsKind(SyntaxKind.OutKeyword) || modifier.IsKind(SyntaxKind.InKeyword))))
                        {
                            results.Add(MethodDiagnostic(
                                method,
                                DiagnosticDescriptors.DNC014_ByRefComposableParameter,
                                declaration.Identifier.GetLocation(),
                                methodName));
                        }

                        ImmutableArray<MethodDeclarationSyntaxExtensions.MethodParameterInfo> parameterInfos =
                            declaration.GetParametersInfos(semanticModel);
                        for (int parameterIndex = 0; parameterIndex < methodSymbol.Parameters.Length; parameterIndex++)
                        {
                            IParameterSymbol parameterSymbol = methodSymbol.Parameters[parameterIndex];
                            AttributeData? composableAttribute = parameterSymbol.GetAttributes()
                                .FirstOrDefault(attribute =>
                                    attribute.AttributeClass?.GetFullMetadataName() == Consts.ComposableAttributeFullName);
                            if (composableAttribute == null)
                                continue;

                            ComposableModeKind declaredMode = parameterSymbol.GetComposableMode();
                            MethodDeclarationSyntaxExtensions.MethodParameterInfo parameterInfo = parameterInfos[parameterIndex];
                            ParameterSyntax parameterSyntax = declaration.ParameterList.Parameters[parameterIndex];
                            if (!declaredMode.IsValidParameterMode() || !parameterInfo.IsComposable)
                            {
                                results.Add(MethodDiagnostic(
                                    method,
                                    DiagnosticDescriptors.DNC020_InvalidComposableMode,
                                    parameterSyntax.GetLocation(),
                                    declaredMode,
                                    "parameter",
                                    parameterSymbol.Name));
                                continue;
                            }

                            if (!parameterInfo.IsInline || declaration.Body == null)
                                continue;

                            foreach (IdentifierNameSyntax use in declaration.Body.DescendantNodes()
                                .OfType<IdentifierNameSyntax>()
                                .Where(identifier => SymbolEqualityComparer.Default.Equals(
                                    semanticModel.GetSymbolInfo(identifier, token).Symbol,
                                    parameterSymbol)))
                            {
                                if (IsAllowedInlineParameterUse(use, declaration, semanticModel, token))
                                    continue;
                                results.Add(MethodDiagnostic(
                                    method,
                                    DiagnosticDescriptors.DNC021_InlineComposableEscape,
                                    use.GetLocation(),
                                    parameterSymbol.Name));
                            }
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

            IncrementalValuesProvider<Diagnostic> compilationDiagnostics = context.CompilationProvider
                .SelectMany(static (compilation, token) =>
                {
                    ImmutableArray<Diagnostic>.Builder diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
                    foreach (SyntaxTree tree in compilation.SyntaxTrees)
                    {
                        SemanticModel model = compilation.GetSemanticModel(tree);
                        SyntaxNode root = tree.GetRoot(token);
                        foreach (MethodDeclarationSyntax declaration in root
                            .DescendantNodes()
                            .OfType<MethodDeclarationSyntax>())
                        {
                            IMethodSymbol? method = model.GetDeclaredSymbol(declaration, token);
                            if (method?.OverriddenMethod?.IsComposableFunction() != true ||
                                method.IsComposableFunction())
                                continue;

                            diagnostics.Add(Diagnostic.Create(
                                DiagnosticDescriptors.DNC023_ComposableOverrideModeMismatch,
                                declaration.Identifier.GetLocation(),
                                method.Name));
                        }

                        foreach (InvocationExpressionSyntax invocation in root
                            .DescendantNodes()
                            .OfType<InvocationExpressionSyntax>())
                        {
                            if (invocation.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault() is not { } containing) continue;
                            IMethodSymbol? containingSymbol = model.GetDeclaredSymbol(containing, token);
                            if (containingSymbol?.GetAttributes().Any(attribute =>
                                attribute.AttributeClass?.GetFullMetadataName() == Consts.ComposableAttributeFullName) == true) continue;
                            if (model.GetSymbolInfo(invocation, token).Symbol is not IMethodSymbol target) continue;
                            if (!target.IsComposableFunction()) continue;
                            diagnostics.Add(Diagnostic.Create(DiagnosticDescriptors.DNC015_DirectComposableCall,
                                invocation.GetLocation(), target.Name));
                        }
                    }
                    return diagnostics.ToImmutable();
                });
            context.RegisterSourceOutput(
                compilationDiagnostics,
                static (spc, diagnostic) => spc.ReportDiagnostic(diagnostic));
        }

        private static MethodResult MethodDiagnostic(
            MethodFullNameAndDeclaration method,
            DiagnosticDescriptor descriptor,
            Location location,
            params object[] messageArguments)
        {
            return new MethodResult(
                method,
                new DiagnosticInfo(
                    descriptor,
                    LocationInfo.FromLocation(location),
                    messageArguments));
        }

        private static bool HasMatchingComposableContract(IMethodSymbol method, IMethodSymbol overridden)
        {
            if (method.GetComposableMode() != overridden.GetComposableMode() ||
                method.Parameters.Length != overridden.Parameters.Length)
                return false;

            for (int index = 0; index < method.Parameters.Length; index++)
            {
                IParameterSymbol parameter = method.Parameters[index];
                IParameterSymbol baseParameter = overridden.Parameters[index];
                bool parameterIsComposable = parameter.GetAttributes().Any(attribute =>
                    attribute.AttributeClass?.GetFullMetadataName() == Consts.ComposableAttributeFullName);
                bool baseIsComposable = baseParameter.GetAttributes().Any(attribute =>
                    attribute.AttributeClass?.GetFullMetadataName() == Consts.ComposableAttributeFullName);
                if (parameterIsComposable != baseIsComposable)
                    return false;
                if (parameterIsComposable && parameter.GetComposableMode() != baseParameter.GetComposableMode())
                    return false;
            }
            return true;
        }

        private static bool IsAllowedInlineParameterUse(
            IdentifierNameSyntax use,
            MethodDeclarationSyntax containingMethod,
            SemanticModel semanticModel,
            System.Threading.CancellationToken token)
        {
            bool isInvocation = use.Parent switch
            {
                InvocationExpressionSyntax invocation => invocation.Expression == use,
                MemberAccessExpressionSyntax memberAccess when memberAccess.Expression == use &&
                    memberAccess.Name.Identifier.ValueText == "Invoke" =>
                    memberAccess.Parent is InvocationExpressionSyntax invocation && invocation.Expression == memberAccess,
                ConditionalAccessExpressionSyntax conditional when conditional.Expression == use &&
                    conditional.WhenNotNull is InvocationExpressionSyntax conditionalInvocation &&
                    conditionalInvocation.Expression is MemberBindingExpressionSyntax memberBinding &&
                    memberBinding.Name.Identifier.ValueText == "Invoke" => true,
                _ => false,
            };

            bool isInlineForward = use.Parent is ArgumentSyntax argument &&
                IsInlineArgument(argument, semanticModel, token);
            if (!isInvocation && !isInlineForward)
                return false;

            foreach (LambdaExpressionSyntax lambda in use.Ancestors().OfType<LambdaExpressionSyntax>())
            {
                if (!lambda.Ancestors().Contains(containingMethod))
                    break;
                if (lambda.Parent is not ArgumentSyntax lambdaArgument ||
                    !IsInlineArgument(lambdaArgument, semanticModel, token))
                    return false;
            }
            return true;
        }

        private static bool IsInlineArgument(
            ArgumentSyntax argument,
            SemanticModel semanticModel,
            System.Threading.CancellationToken token)
        {
            if (argument.Parent?.Parent is not InvocationExpressionSyntax invocation ||
                semanticModel.GetSymbolInfo(invocation, token).Symbol is not IMethodSymbol target)
                return false;

            ImmutableArray<MethodDeclarationSyntaxExtensions.MethodParameterInfo> parameters =
                target.GetParametersInfos(semanticModel);
            if (argument.NameColon != null)
            {
                string name = argument.NameColon.Name.Identifier.ValueText;
                return parameters.FirstOrDefault(parameter => parameter.Name == name)?.IsInline == true;
            }

            int argumentIndex = invocation.ArgumentList.Arguments.IndexOf(argument);
            return argumentIndex >= 0 && argumentIndex < parameters.Length && parameters[argumentIndex].IsInline;
        }

        private static bool HasGeneratedSignatureConflict(IMethodSymbol method)
        {
            return method.ContainingType.GetMembers(method.Name)
                .OfType<IMethodSymbol>()
                .Any(candidate => !SymbolEqualityComparer.Default.Equals(candidate, method) &&
                    candidate.Arity == method.Arity &&
                    candidate.Parameters.Length == method.Parameters.Length + 3 &&
                    method.Parameters.Select((parameter, index) =>
                        MatchesGeneratedParameter(parameter, candidate.Parameters[index])).All(match => match) &&
                    candidate.Parameters[candidate.Parameters.Length - 3].Type.GetFullMetadataName() == Consts.ComposeContext.FullName &&
                    candidate.Parameters[candidate.Parameters.Length - 2].Type.GetFullMetadataName() ==
                        Consts.ComposableArgumentsState.FullName &&
                    candidate.Parameters[candidate.Parameters.Length - 1].Type.GetFullMetadataName() ==
                        Consts.ComposableArgumentsDefaultState.FullName);
        }

        private static bool MatchesGeneratedParameter(IParameterSymbol source, IParameterSymbol candidate)
        {
            if (source.RefKind != candidate.RefKind)
                return false;
            bool isComposable = source.GetAttributes().Any(attribute =>
                attribute.AttributeClass?.GetFullMetadataName() == Consts.ComposableAttributeFullName);
            if (!isComposable)
                return SymbolEqualityComparer.Default.Equals(source.Type, candidate.Type);
            if (source.Type is not INamedTypeSymbol sourceDelegate || candidate.Type is not INamedTypeSymbol generatedDelegate)
                return false;
            if (generatedDelegate.Name != "ComposableAction" ||
                generatedDelegate.ContainingNamespace?.ToDisplayString() != "DotNetCompose.Runtime" ||
                sourceDelegate.TypeArguments.Length != generatedDelegate.TypeArguments.Length)
                return false;
            return sourceDelegate.TypeArguments.Zip(generatedDelegate.TypeArguments,
                (left, right) => SymbolEqualityComparer.Default.Equals(left, right)).All(equal => equal);
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
