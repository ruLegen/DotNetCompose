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
                string hintName = new string(typeName.Select(character => char.IsLetterOrDigit(character) ? character : '_').ToArray());
                spc.AddSource($"{hintName}.DuplicatedMethods.g.cs",
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
            SemanticModel firstSemanticModel = compilation.GetSemanticModel(firstMethod.SyntaxTree);
            IMethodSymbol methodSymbol = firstSemanticModel.GetDeclaredSymbol(firstMethod);
            INamedTypeSymbol containingType = methodSymbol?.ContainingType;

            if (containingType == null)
                return string.Empty;

            string namespaceName = containingType.ContainingNamespace?.ToDisplayString() ?? string.Empty;
            string typeName = containingType.Name;
            string accessibility = containingType.DeclaredAccessibility.ToString().ToLower();
            TypeDeclarationSyntax? typeDeclaration = firstMethod.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();

            SyntaxNode root = firstMethod.SyntaxTree.GetRoot();
            ImmutableArray<UsingDirectiveSyntax> usings = root.DescendantNodes()
                .OfType<UsingDirectiveSyntax>()
                .Distinct(UsingDerectiveComparerByName.Default)
                .ToImmutableArray();

            var rewrittenMethods = typeMethods.Select(m => m.Declaration!)
                .Select(m =>
                {
                    SemanticModel semanticModel = compilation.GetSemanticModel(m.SyntaxTree);
                    IMethodSymbol symbol = semanticModel.GetDeclaredSymbol(m)!;
                    var methodParams = m.GetParametersInfos(semanticModel);
                    RewriterOptions options = new RewriterOptions(
                        Consts.Rewriter.ContextParamName,
                        Consts.Rewriter.ChangedParamName,
                        Consts.Rewriter.DefaultParamName,
                        Consts.Rewriter.StoredLambdaClassName,
                        Consts.Rewriter.BuildersClassName);

                    string methodName = m.Identifier.ValueText;
                    var typeParamNames = m.TypeParameterList?.Parameters
                        .Select(tp => tp.Identifier.ValueText)
                        .ToImmutableArray() ?? ImmutableArray<string>.Empty;

                    MethodGenerationContext methodCtx = new MethodGenerationContext(
                        methodName,
                        typeParamNames,
                        methodParams,
                        methodParams.Any(p => p.DefaultProviderType != null),
                        symbol.GetComposableMode());

                    int initialGroupId = RewriterSession.DeterministicHash(m.GetMethodID(semanticModel));
                    RewriterSession session = new RewriterSession(initialGroupId, diagnostics, methodCtx.IsReadOnly);

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
                return (pair.Session, pair.Symbol, MethodBody: pair.Session.HasErrors ? null : body);
            })
            .Where(x => x.MethodBody != null)
            .ToImmutableArray();

            if (!rewrittenMethods.Any())
                return string.Empty;

            ImmutableArray<SyntaxNode> instanceMethods = rewrittenMethods
                .Where(item => !item.Symbol.IsStatic)
                .Select(item => (SyntaxNode)AddEditorBrowsable((MethodDeclarationSyntax)item.MethodBody!))
                .ToImmutableArray();
            ImmutableArray<SyntaxNode> builderMethods = rewrittenMethods
                .Where(item => item.Symbol.IsStatic)
                .Select(item => item.MethodBody!)
                .Concat(rewrittenMethods
                    .Where(item => !item.Symbol.IsStatic)
                    .Select(item => (SyntaxNode)CreateInstanceBridge((MethodDeclarationSyntax)item.MethodBody!, item.Symbol)))
                .ToImmutableArray();

            CodeGenerationInput input = new CodeGenerationInput(
                Namespace: namespaceName,
                TypeName: typeName,
                Accessibility: accessibility,
                TypeParameters: typeDeclaration?.TypeParameterList,
                TypeConstraints: typeDeclaration?.ConstraintClauses ?? default,
                Usings: usings,
                InstanceMethods: instanceMethods,
                BuilderMethods: builderMethods,
                Sessions: rewrittenMethods.Select(p => p.Session).ToImmutableArray());

            var emitter = new DefaultCodeEmitter();
            return emitter.Emit(input);
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
                                SyntaxFactory.IdentifierName("Never"))))));
            return method.AddAttributeLists(
                SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(attribute)));
        }

        private static MethodDeclarationSyntax CreateInstanceBridge(MethodDeclarationSyntax method, IMethodSymbol symbol)
        {
            string receiverName = "__instance";
            while (method.ParameterList.Parameters.Any(parameter => parameter.Identifier.ValueText == receiverName))
                receiverName += "_";

            ParameterSyntax receiver = SyntaxFactory.Parameter(SyntaxFactory.Identifier(receiverName))
                .WithType(SyntaxFactory.ParseTypeName(symbol.ContainingType.ToDisplayString(
                    SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Included))));
            ParameterListSyntax parameters = method.ParameterList.WithParameters(
                method.ParameterList.Parameters.Insert(0, receiver));

            SimpleNameSyntax name = method.TypeParameterList == null
                ? SyntaxFactory.IdentifierName(method.Identifier)
                : SyntaxFactory.GenericName(method.Identifier,
                    SyntaxFactory.TypeArgumentList(
                        SyntaxFactory.SeparatedList<TypeSyntax>(method.TypeParameterList.Parameters
                            .Select(parameter => SyntaxFactory.IdentifierName(parameter.Identifier) as TypeSyntax))));
            InvocationExpressionSyntax invocation = SyntaxFactory.InvocationExpression(
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    SyntaxFactory.IdentifierName(receiverName),
                    name),
                SyntaxFactory.ArgumentList(
                    SyntaxFactory.SeparatedList(method.ParameterList.Parameters
                        .Select(parameter => SyntaxFactory.Argument(SyntaxFactory.IdentifierName(parameter.Identifier))))));
            StatementSyntax statement = symbol.ReturnsVoid
                ? (StatementSyntax)SyntaxFactory.ExpressionStatement(invocation)
                : SyntaxFactory.ReturnStatement(invocation);

            SyntaxTokenList modifiers = SyntaxFactory.TokenList(method.Modifiers.Where(token =>
                token.IsKind(SyntaxKind.PublicKeyword) || token.IsKind(SyntaxKind.InternalKeyword) ||
                token.IsKind(SyntaxKind.ProtectedKeyword) || token.IsKind(SyntaxKind.PrivateKeyword)))
                .Add(SyntaxFactory.Token(SyntaxKind.StaticKeyword));
            return method
                .WithModifiers(modifiers)
                .WithParameterList(parameters)
                .WithBody(SyntaxFactory.Block(statement))
                .WithExpressionBody(null)
                .WithSemicolonToken(default);
        }
    }
}
