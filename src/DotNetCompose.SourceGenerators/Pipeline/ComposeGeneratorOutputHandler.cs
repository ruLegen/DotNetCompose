using DotNetCompose.SourceGenerators.Diagnostics;
using DotNetCompose.SourceGenerators.Emitters;
using DotNetCompose.SourceGenerators.Extensions;
using DotNetCompose.SourceGenerators.Helpers;
using DotNetCompose.SourceGenerators.Rewriters;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.IO;
using System.Text;
using static DotNetCompose.SourceGenerators.ComposeSourceGenerator;

namespace DotNetCompose.SourceGenerators.Pipeline
{
    internal sealed class ComposeGeneratorOutputHandler : IOutputHandler
    {
        public void Handle(SourceProductionContext spc, Compilation compilation, ClassAndComposablesMethods input, PipelineContext context)
        {
            DiagnosticReporter reporter = new DiagnosticReporter();
            foreach (var source in GenerateComposableMethods(input, compilation, reporter, context))
                spc.AddSource(source.HintName, SourceText.From(source.Code, Encoding.UTF8));

            foreach (DiagnosticInfo diag in reporter.ToImmutable())
                spc.ReportDiagnostic(diag.ToDiagnostic());
        }

        private static IEnumerable<(string HintName, string Code)> GenerateComposableMethods(
            ClassAndComposablesMethods classAndComposablesMethods,
            Compilation compilation,
            IDiagnosticReporter diagnostics,
            PipelineContext pipelineContext)
        {
            ImmutableArray<MethodFullNameAndDeclaration> typeMethods = classAndComposablesMethods.Methods;
            if (!typeMethods.Any())
                yield break;

            // Number methods once for the whole type so lambda names cannot collide across parts.
            var rewrittenMethods = typeMethods.Select(m => m.Declaration!)
                .OrderBy(m => m.SyntaxTree.FilePath, StringComparer.Ordinal)
                .ThenBy(m => GetTreeIndex(compilation, m.SyntaxTree))
                .ThenBy(m => m.SpanStart)
                .Select((m, methodIndex) =>
                {
                    SemanticModel semanticModel = compilation.GetSemanticModel(m.SyntaxTree);
                    IMethodSymbol symbol = semanticModel.GetDeclaredSymbol(m)!;
                    var methodParams = m.GetParametersInfos(semanticModel);
                    RewriterOptions options = new RewriterOptions(
                        Consts.Rewriter.ContextParamName,
                        Consts.Rewriter.ChangedParamName,
                        Consts.Rewriter.DefaultParamName,
                        Consts.Rewriter.StoredLambdaClassName,
                        Consts.Rewriter.BuildersClassName,
                        ArgumentStateBuffer.SelectStorage(compilation, (CSharpParseOptions)m.SyntaxTree.Options,
                            pipelineContext.UseStackAllocForArgumentStates));

                    string methodName = m.Identifier.ValueText;
                    string methodIdentity = m.GetMethodID(semanticModel);
                    string diagnosticsIdentity = symbol.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
                        "." + symbol.MetadataName + "(" +
                        string.Join(",", symbol.Parameters.Select(parameter =>
                            parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))) + ")";
                    FileLinePositionSpan sourceSpan = m.Identifier.GetLocation().GetMappedLineSpan();
                    string sourcePath = MakeProjectRelativePath(sourceSpan.Path, pipelineContext.ProjectDirectory);
                    var typeParamNames = m.TypeParameterList?.Parameters
                        .Select(tp => tp.Identifier.ValueText)
                        .ToImmutableArray() ?? ImmutableArray<string>.Empty;

                    MethodGenerationContext methodCtx = new MethodGenerationContext(
                        methodName,
                        typeParamNames,
                        methodParams,
                        methodParams.Any(p => p.DefaultProviderType != null),
                        symbol.GetComposableMode(),
                        pipelineContext.GenerateDiagnostics,
                        RewriterSession.DeterministicHash64(diagnosticsIdentity),
                        sourcePath,
                        sourceSpan.StartLinePosition.Line + 1);

                    int initialGroupId = RewriterSession.DeterministicHash(methodIdentity);
                    RewriterSession session = new RewriterSession(
                        initialGroupId, methodIndex, diagnostics, methodCtx.IsReadOnly);

                    return (
                        Options: options,
                        MethodCtx: methodCtx,
                        Session: session,
                        Method: m,
                        SemanticModel: semanticModel,
                        Symbol: symbol);
                })
            .Select(pair =>
            {
                SyntaxNode body = ComposeSyntaxRewriter.Rewrite(
                    pair.Options, pair.MethodCtx, pair.Session, pair.SemanticModel, pair.Method,
                    pipelineContext.MethodCallHandlers, pipelineContext.WellKnownRegistry,
                    pipelineContext.Strategies);
                return (pair.Session, pair.Symbol,
                    Declaration: pair.Method.Ancestors().OfType<TypeDeclarationSyntax>().First(),
                    MethodBody: pair.Session.HasErrors ? null : body);
            })
            .Where(x => x.MethodBody != null)
            .ToImmutableArray();

            // A partial declaration retains its own imports, constraints and stored lambdas.
            foreach (var part in rewrittenMethods.GroupBy(item => item.Declaration))
            {
                TypeDeclarationSyntax declaration = part.Key;
                INamedTypeSymbol containingType = part.First().Symbol.ContainingType;
                var input = new CodeGenerationInput(
                    SourceContext: SourceDeclarationContext.Create(declaration),
                    TypeName: declaration.Identifier.Text,
                    Accessibility: containingType.DeclaredAccessibility.ToString().ToLower(),
                    TypeParameters: declaration.TypeParameterList,
                    TypeConstraints: declaration.ConstraintClauses,
                    InstanceMethods: part.Where(item => !item.Symbol.IsStatic)
                        .Select(item => (SyntaxNode)AddEditorBrowsable((MethodDeclarationSyntax)item.MethodBody!))
                        .ToImmutableArray(),
                    BuilderMethods: part.Where(item => item.Symbol.IsStatic).Select(item => item.MethodBody!)
                        .ToImmutableArray(),
                    InstanceSessions: part.Where(item => !item.Symbol.IsStatic).Select(item => item.Session).ToImmutableArray(),
                    BuilderSessions: part.Where(item => item.Symbol.IsStatic).Select(item => item.Session).ToImmutableArray(),
                    SupportsEnhancedLineDirectives: pipelineContext.SupportsEnhancedLineDirectives);

                yield return (CreateHintName(classAndComposablesMethods.ClassName, containingType,
                    declaration, compilation, pipelineContext.ProjectDirectory), new DefaultCodeEmitter().Emit(input));
            }
        }

        private static string CreateHintName(string typeName, INamedTypeSymbol symbol,
            TypeDeclarationSyntax declaration, Compilation compilation, string projectDirectory)
        {
            string path = MakeProjectRelativePath(declaration.SyntaxTree.FilePath, projectDirectory).Replace('\\', '/');
            // In-memory trees may lack paths or share one; distinguish those without hashing their contents.
            if (string.IsNullOrEmpty(path) || compilation.SyntaxTrees.Count(tree => tree.FilePath == declaration.SyntaxTree.FilePath) > 1)
                path += "#tree" + GetTreeIndex(compilation, declaration.SyntaxTree);
            int ordinal = symbol.DeclaringSyntaxReferences
                .Where(reference => reference.SyntaxTree == declaration.SyntaxTree)
                .OrderBy(reference => reference.Span.Start)
                .TakeWhile(reference => reference.Span.Start != declaration.SpanStart).Count();
            string prefix = new string(typeName.Select(character => char.IsLetterOrDigit(character) ? character : '_').ToArray());
            long hash = RewriterSession.DeterministicHash64(typeName + "\n" + path);
            return $"{prefix}.{hash:x16}.{ordinal}.DuplicatedMethods.g.cs";
        }

        private static int GetTreeIndex(Compilation compilation, SyntaxTree tree)
            => compilation.SyntaxTrees.TakeWhile(candidate => candidate != tree).Count();

        private static string MakeProjectRelativePath(string path, string projectDirectory)
        {
            if (string.IsNullOrEmpty(path))
                return string.Empty;
            if (string.IsNullOrEmpty(projectDirectory))
                return path;
            try
            {
                string fullPath = Path.GetFullPath(path);
                string fullProjectDirectory = Path.GetFullPath(projectDirectory)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (fullPath.StartsWith(fullProjectDirectory, StringComparison.OrdinalIgnoreCase))
                    return fullPath.Substring(fullProjectDirectory.Length).Replace(Path.DirectorySeparatorChar, '/');
            }
            catch
            {
                // Preserve the compiler-provided path when normalization is not possible.
            }
            return path;
        }

        private static MethodDeclarationSyntax AddEditorBrowsable(MethodDeclarationSyntax method)
        {
            AttributeSyntax attribute = SyntaxFactory.Attribute(
                SyntaxFactory.ParseName("global::System.ComponentModel.EditorBrowsable"),
                SyntaxFactory.AttributeArgumentList(
                    SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.AttributeArgument(
                            SyntaxFactory.MemberAccessExpression(
                                SyntaxKind.SimpleMemberAccessExpression,
                                SyntaxFactory.ParseName("global::System.ComponentModel.EditorBrowsableState"),
                                SyntaxFactory.IdentifierName(Consts.EditorBrowsable.NeverField))))));
            return method.AddAttributeLists(
                SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(attribute)));
        }
    }
}
