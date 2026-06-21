using DotNetCompose.SourceGenerators.Extensions;
using DotNetCompose.SourceGenerators.Handlers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using static DotNetCompose.SourceGenerators.Consts;
using static DotNetCompose.SourceGenerators.Extensions.MethodDeclarationSyntaxExtensions;
using static DotNetCompose.SourceGenerators.Consts;

#nullable enable
namespace DotNetCompose.SourceGenerators.Rewriters
{
	internal class ComposeMethodRewriter : ComposableSyntaxRewriterBase
	{
		internal ComposeMethodRewriter(
			RewriterOptions options,
			MethodGenerationContext methodCtx,
			RewriterSession session,
			SemanticModel semanticModel,
			IReadOnlyList<IMethodCallHandler> methodCallHandlers,
			WellKnownFunctionRegistry wellKnownRegistry)
			: base(options, methodCtx, session, semanticModel, methodCallHandlers, wellKnownRegistry)
		{
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
			ComposeMethodRewriter rewriter = new ComposeMethodRewriter(options, methodCtx, session, semanticModel, methodCallHandlers, wellKnownRegistry);
			return rewriter.Visit(method);
		}

		internal static ParameterListSyntax ReplaceAllComposableParameters(MethodDeclarationSyntax method, SemanticModel semanticModel, bool addAttributeToComposableParameters)
		{
			var rewriter = new ComposeMethodRewriter(RewriterOptions.Default,
				new MethodGenerationContext(ImmutableArray<MethodParameterInfo>.Empty, false, false),
				new RewriterSession(0),
				semanticModel, Array.Empty<IMethodCallHandler>(), WellKnownFunctionRegistry.Empty);
			return rewriter.ReplaceAllComposableParameters(method, addAttributeToComposableParameters);
		}

		internal static ParameterListSyntax AppendComposableContextrelatedParameters(ParameterListSyntax paramList, SemanticModel semanticModel, string contextParamName, string changedParamName)
		{
			var options = new RewriterOptions(contextParamName, changedParamName, Consts.Rewriter.DefaultParamName,
				Consts.Rewriter.StoredLambdaClassName, Consts.Rewriter.BuildersClassName);
			var rewriter = new ComposeMethodRewriter(options,
				new MethodGenerationContext(ImmutableArray<MethodParameterInfo>.Empty, false, false),
				new RewriterSession(0),
				semanticModel, Array.Empty<IMethodCallHandler>(), WellKnownFunctionRegistry.Empty);
			return rewriter.AppendComposableContextrelatedParameters(paramList, contextParamName, changedParamName);
		}
	}
}
