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
        private readonly TransformationContext _ctx;
        private readonly ISignatureRewriter _signatureRewriter;
        private readonly IBodyWrappingStrategy _bodyWrapping;
        private readonly IControlFlowStrategy _controlFlow;
        private readonly ICallInterceptionStrategy _callInterceptor;
        private readonly IDefaultValueSubstitutor _defaultValueSubstitutor;
        private readonly IStableParameterOptimizer _stableParameterOptimizer;

        private ComposeSyntaxRewriter(
            TransformationContext ctx,
            ISignatureRewriter signatureRewriter,
            IBodyWrappingStrategy bodyWrapping,
            IControlFlowStrategy controlFlow,
            ICallInterceptionStrategy callInterceptor,
            IDefaultValueSubstitutor defaultValueSubstitutor,
            IStableParameterOptimizer stableParameterOptimizer)
            : base()
        {
            _ctx = ctx;
            _signatureRewriter = signatureRewriter;
            _bodyWrapping = bodyWrapping;
            _controlFlow = controlFlow;
            _callInterceptor = callInterceptor;
            _defaultValueSubstitutor = defaultValueSubstitutor;
            _stableParameterOptimizer = stableParameterOptimizer;
        }

        public override SyntaxNode VisitMethodDeclaration(MethodDeclarationSyntax method)
        {
            var sourceLocationAnnotation = method.CreateLocationSyntaxAnnotation();
            var newMethod = _signatureRewriter.RewriteSignature(method, _ctx);

            if (method.Body != null)
            {
                BlockSyntax transformedBody = (BlockSyntax)Visit(method.Body);
                newMethod = newMethod.WithBody(transformedBody);
            }
            else if (method.ExpressionBody != null)
            {
                _ctx.Session.Report(DiagnosticInfo.Create(
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

                visitedBody = _defaultValueSubstitutor.SubstituteDefaults(visitedBody, _ctx);
                visitedBody = _stableParameterOptimizer.OptimizeStableParameters(visitedBody, _ctx);
                visitedBody = _bodyWrapping.WrapMethodBody(visitedBody, _ctx);

                return visitedBody;
            }
            return base.VisitBlock(node);
        }

        public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            IMethodSymbol? methodSymbol = _ctx.SemanticModel.GetSymbolInfo(node).Symbol as IMethodSymbol;
            if (methodSymbol != null)
            {
                var replacement = _callInterceptor.TryInterceptInvocation(node, _ctx);
                if (replacement != null)
                    return replacement;
            }
            return base.VisitInvocationExpression(node);
        }

        public override SyntaxNode? VisitConditionalAccessExpression(ConditionalAccessExpressionSyntax node)
        {
            var replacement = _callInterceptor.TryInterceptConditionalAccess(node, _ctx);
            if (replacement != null)
                return replacement;
            return base.VisitConditionalAccessExpression(node);
        }

        public override SyntaxNode VisitIfStatement(IfStatementSyntax node)
        {
            using var _ = _ctx.Session.EnterConditional();
            var visited = (IfStatementSyntax)base.VisitIfStatement(node);
            return _controlFlow.RewriteIf(visited, _ctx);
        }

        public override SyntaxNode VisitForStatement(ForStatementSyntax node)
        {
            var visited = (ForStatementSyntax)base.VisitForStatement(node);
            return _controlFlow.RewriteFor(visited, _ctx);
        }

        public override SyntaxNode VisitForEachStatement(ForEachStatementSyntax node)
        {
            var visited = (ForEachStatementSyntax)base.VisitForEachStatement(node);
            return _controlFlow.RewriteForEach(visited, _ctx);
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
            WellKnownFunctionRegistry wellKnownRegistry)
        {
            var ctx = new TransformationContext(
                semanticModel,
                options,
                methodCtx,
                session,
                methodCallHandlers,
                wellKnownRegistry,
                n => n);

            var rewriter = new ComposeSyntaxRewriter(
                ctx,
                new DefaultSignatureRewriter(),
                new DefaultBodyWrappingStrategy(),
                new DefaultControlFlowStrategy(),
                new DefaultCallInterceptionStrategy(),
                new DefaultValueSubstitutor(),
                new DefaultStableParameterOptimizer());

            ctx.SetVisitNode(rewriter.Visit);

            try
            {
                var result = rewriter.Visit(method);
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
