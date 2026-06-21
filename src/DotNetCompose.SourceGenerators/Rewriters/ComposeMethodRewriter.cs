using DotNetCompose.SourceGenerators.Extensions;
using DotNetCompose.SourceGenerators.Handlers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using static DotNetCompose.SourceGenerators.Consts;

#nullable enable
namespace DotNetCompose.SourceGenerators.Rewriters
{
	internal class ComposeMethodRewriter : ComposableSyntaxRewriterBase
	{
		internal ComposeMethodRewriter(
			ComposableMethodGeneratorContext ctx,
			SemanticModel semanticModel,
			IReadOnlyList<IMethodCallHandler> methodCallHandlers)
			: base(ctx, semanticModel, methodCallHandlers)
		{
		}

		internal static SyntaxNode? Rewrite(
			ComposableMethodGeneratorContext ctx,
			SemanticModel semanticModel,
			MethodDeclarationSyntax method,
			IReadOnlyList<IMethodCallHandler> methodCallHandlers)
		{
			ComposeMethodRewriter rewriter = new ComposeMethodRewriter(ctx, semanticModel, methodCallHandlers);
			return rewriter.Visit(method);
		}

		internal static ParameterListSyntax ReplaceAllComposableParameters(MethodDeclarationSyntax method, SemanticModel semanticModel, bool addAttributeToComposableParameters)
		{
			var tempCtx = new ComposableMethodGeneratorContext(Consts.Rewriter.ContextParamName, Consts.Rewriter.ChangedParamName);
			var rewriter = new ComposeMethodRewriter(tempCtx, semanticModel, Array.Empty<IMethodCallHandler>());
			return rewriter.ReplaceAllComposableParameters(method, addAttributeToComposableParameters);
		}

		internal static ParameterListSyntax AppendComposableContextrelatedParameters(ParameterListSyntax paramList, SemanticModel semanticModel, string contextParamName, string changedParamName)
		{
			var tempCtx = new ComposableMethodGeneratorContext(contextParamName, changedParamName);
			var rewriter = new ComposeMethodRewriter(tempCtx, semanticModel, Array.Empty<IMethodCallHandler>());
			return rewriter.AppendComposableContextrelatedParameters(paramList, contextParamName, changedParamName);
		}
	}
}
