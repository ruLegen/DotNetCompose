using DotNetCompose.SourceGenerators.Diagnostics;
using DotNetCompose.SourceGenerators.Extensions;
using DotNetCompose.SourceGenerators.Handlers;
using DotNetCompose.SourceGenerators.Pipeline;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using static DotNetCompose.SourceGenerators.Consts;
using static DotNetCompose.SourceGenerators.Extensions.MethodDeclarationSyntaxExtensions;

#nullable enable
namespace DotNetCompose.SourceGenerators.Rewriters
{
    internal sealed class ComposeSyntaxRewriter : CSharpSyntaxRewriter
    {
        private ComposeSyntaxRewriter(StrategyContainer strategies)
            : base()
        {
            _signatureRewriter = strategies.SignatureRewriter;
            _bodyWrapping = strategies.BodyWrapping;
            _controlFlow = strategies.ControlFlow;
            _callInterceptor = strategies.CallInterception;
            _defaultValueSubstitutor = strategies.DefaultValueSubstitutor;
            _parameterChangeTransformer = strategies.StableParameterCheckTransformer;
        }

        private TransformationContext? _ctx;
        private readonly ISignatureRewriter _signatureRewriter;
        private readonly IBodyWrappingStrategy _bodyWrapping;
        private readonly IControlFlowStrategy _controlFlow;
        private readonly ICallInterceptionStrategy _callInterceptor;
        private readonly IDefaultValueSubstitutor _defaultValueSubstitutor;
        private readonly IParameterChangedTransformer _parameterChangeTransformer;

        public void SetContext(TransformationContext ctx) => _ctx = ctx;
        private TransformationContext Ctx => _ctx!;

        public override SyntaxNode VisitMethodDeclaration(MethodDeclarationSyntax method)
        {
            var sourceLocationAnnotation = method.CreateLocationSyntaxAnnotation();
            var newMethod = _signatureRewriter.RewriteSignature(method, Ctx);

            if (method.Body != null)
            {
                BlockSyntax transformedBody = (BlockSyntax)Visit(method.Body);
                newMethod = newMethod.WithBody(transformedBody);
            }
            else if (method.ExpressionBody != null)
            {
                Ctx.Session.Report(DiagnosticInfo.Create(
                    DiagnosticDescriptors.DNC001_ExpressionBodiedNotSupported,
                    method.ExpressionBody.GetLocation(),
                    method.Identifier.ValueText));
                newMethod = newMethod.WithBody(SyntaxFactory.Block());
            }

            if (sourceLocationAnnotation != null)
                newMethod = newMethod.WithAdditionalAnnotations(sourceLocationAnnotation);

            return newMethod;
        }

        public override SyntaxNode VisitBlock(BlockSyntax node)
        {
            if (node.Parent is MethodDeclarationSyntax)
            {
                var visitedBody = (BlockSyntax)base.VisitBlock(node);

                visitedBody = _defaultValueSubstitutor.SubstituteDefaults(visitedBody, Ctx);
                visitedBody = _parameterChangeTransformer.TransformParameters(visitedBody, Ctx);
                visitedBody = _bodyWrapping.WrapMethodBody(visitedBody, Ctx);

                return visitedBody;
            }
            return base.VisitBlock(node);
        }

        public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            IMethodSymbol? methodSymbol = Ctx.SemanticModel.GetSymbolInfo(node).Symbol as IMethodSymbol;
            if (methodSymbol != null)
            {
                var replacement = _callInterceptor.TryInterceptInvocation(node, Ctx);
                if (replacement != null)
                    return replacement;
            }
            return base.VisitInvocationExpression(node);
        }

        public override SyntaxNode? VisitConditionalAccessExpression(ConditionalAccessExpressionSyntax node)
        {
            var replacement = _callInterceptor.TryInterceptConditionalAccess(node, Ctx);
            if (replacement != null)
                return replacement;
            return base.VisitConditionalAccessExpression(node);
        }

        public override SyntaxNode VisitIfStatement(IfStatementSyntax node)
        {
            using var _ = Ctx.Session.EnterConditional();
            var visited = (IfStatementSyntax)base.VisitIfStatement(node);
            return _controlFlow.RewriteIf(visited, Ctx);
        }

        public override SyntaxNode VisitForStatement(ForStatementSyntax node)
        {
            var visited = (ForStatementSyntax)base.VisitForStatement(node);
            return _controlFlow.RewriteFor(visited, Ctx);
        }

        public override SyntaxNode VisitForEachStatement(ForEachStatementSyntax node)
        {
            var visited = (ForEachStatementSyntax)base.VisitForEachStatement(node);
            return _controlFlow.RewriteForEach(visited, Ctx);
        }

        public override SyntaxNode? VisitExpressionStatement(ExpressionStatementSyntax node)
        {
            var locationAnnotation = node.CreateLocationSyntaxAnnotation();
            var processed = base.VisitExpressionStatement(node);
            if (locationAnnotation != null && processed != null)
                processed = processed.WithAdditionalAnnotations(locationAnnotation);
            return processed;
        }

        public override SyntaxNode? VisitVariableDeclaration(VariableDeclarationSyntax node)
        {
            var locationAnnotation = node.CreateLocationSyntaxAnnotation();
            var processed = base.VisitVariableDeclaration(node);
            if (locationAnnotation != null && processed != null)
                processed = processed.WithAdditionalAnnotations(locationAnnotation);
            return processed;
        }

        internal static SyntaxNode? Rewrite(
            RewriterOptions options,
            MethodGenerationContext methodCtx,
            RewriterSession session,
            SemanticModel semanticModel,
            MethodDeclarationSyntax method,
            IReadOnlyList<IMethodCallHandler> methodCallHandlers,
            WellKnownFunctionRegistry wellKnownRegistry,
            StrategyContainer? strategies = null)
        {
            strategies ??= StrategyContainer.Default;

            var rewriter = new ComposeSyntaxRewriter(strategies);

            NodeTransformer transformer = new NodeTransformer(rewriter.Visit);

            var ctx = new TransformationContext(
                semanticModel,
                options,
                methodCtx,
                session,
                methodCallHandlers,
                wellKnownRegistry,
                transformer,
                strategies);

            rewriter.SetContext(ctx);

            try
            {
                SyntaxNode result = rewriter.Visit(method);
                return result;
            }
            catch (Exception ex)
            {
                session.Report(DiagnosticInfo.Create(
                    DiagnosticDescriptors.DNC900_InternalError,
                    method.GetLocation(),
                    ex.ToString()));
                return null;
            }
        }
    }
}
