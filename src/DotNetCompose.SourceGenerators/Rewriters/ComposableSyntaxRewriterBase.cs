using DotNetCompose.SourceGenerators.Diagnostics;
using DotNetCompose.SourceGenerators.Extensions;
using DotNetCompose.SourceGenerators.Handlers;
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
	internal abstract class ComposableSyntaxRewriterBase : CSharpSyntaxRewriter
	{
		protected ComposableSyntaxRewriterBase(
			RewriterOptions options,
			MethodGenerationContext methodCtx,
			RewriterSession session,
			SemanticModel semanticModel,
			IReadOnlyList<IMethodCallHandler> methodCallHandlers,
			WellKnownFunctionRegistry wellKnownRegistry)
		{
			_options = options;
			_methodCtx = methodCtx;
			_session = session;
			_semanticModel = semanticModel;
			_methodCallHandlers = methodCallHandlers;
			_wellKnownRegistry = wellKnownRegistry;
		}
		protected readonly RewriterOptions _options;
		protected readonly MethodGenerationContext _methodCtx;
		protected readonly RewriterSession _session;
		protected SemanticModel _semanticModel;
		protected readonly IReadOnlyList<IMethodCallHandler> _methodCallHandlers;
		protected readonly WellKnownFunctionRegistry _wellKnownRegistry;

		public override SyntaxNode VisitMethodDeclaration(MethodDeclarationSyntax method)
		{
			var sourceLocationAnnotation = method.CreateLocationSyntaxAnnotation();
			var methodModifiers = method.Modifiers;

			bool hasAnyComposables = _methodCtx.Parameters.Any(p => p.IsComposable);

			ParameterListSyntax newParameterList = method.ParameterList;
			if (hasAnyComposables)
			{
				newParameterList = ReplaceAllComposableParameters(method, true);
			}
			if (_methodCtx.HasDefaultParams)
			{
				newParameterList = newParameterList.WithParameters(
					SyntaxFactory.SeparatedList(
						newParameterList.Parameters.Select((p, i) =>
							i < _methodCtx.Parameters.Length && _methodCtx.Parameters[i].DefaultProviderType != null
								? p.WithDefault(null)
								: p)));
			}
			newParameterList = AppendComposableContextrelatedParameters(newParameterList, _options.ContextVarName, _options.ChangedVarName);

			MethodDeclarationSyntax newMethod = method
				.WithParameterList(newParameterList)
				.WithModifiers(methodModifiers)
				.WithAttributeLists(ReplaceComposableAttribute(method.AttributeLists));

			if (method.Body != null)
			{
				BlockSyntax transformedBody = base.Visit(method.Body) as BlockSyntax;
				newMethod = newMethod.WithBody(transformedBody);
			}
			else if (method.ExpressionBody != null)
			{
				_session.Report(DiagnosticInfo.Create(
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
				return VisitMethodDeclarationBlock(node);
			}
			return base.VisitBlock(node);
		}

		protected SyntaxNode VisitMethodDeclarationBlock(BlockSyntax node)
		{
			string ctxVar = _options.ContextVarName;
			string changedVar = _options.ChangedVarName;
			using ListPoolObject<StatementSyntax> syntaxList = ListPool<StatementSyntax>.Get();

			using ListPoolObject<StatementSyntax> tryStatements = ListPool<StatementSyntax>.Get();

			tryStatements.Add(SyntaxFactoryHelpers.CreateSafeMethodCallOnVariableWithArgs(
				ctxVar,
				Consts.ComposeContext.StartRestartableGroupMethod,
				SyntaxFactoryHelpers.CreateIntLiteral(_session.InitialGroupId)));

			var normalParams = _methodCtx.Parameters
				.Select((p, i) => (Param: p, Index: i))
				.Where(x => !x.Param.IsComposable)
				.ToList();

			bool anyNormalParams = normalParams.Any();
			bool allStable = anyNormalParams
				&& normalParams.All(x => x.Param.Type != null && x.Param.Type.IsStableType());

			// DIAG: check allStable for all methods with normal params

			BlockSyntax processedBody = base.VisitBlock(node) as BlockSyntax;

			if (_methodCtx.HasDefaultParams)
			{
				using ListPoolObject<StatementSyntax> substStmts = ListPool<StatementSyntax>.Get();
				for (int i = 0; i < _methodCtx.Parameters.Length; i++)
				{
					var p = _methodCtx.Parameters[i];
					if (p.DefaultProviderType == null) continue;

					string providerTypeName = p.DefaultProviderType.ToDisplayString(
						SymbolDisplayFormat.FullyQualifiedFormat
							.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Included));

					var condition = SyntaxFactory.BinaryExpression(
						SyntaxKind.EqualsExpression,
						SyntaxFactory.ElementAccessExpression(
							SyntaxFactory.IdentifierName(Consts.Rewriter.DefaultParamName))
						.WithArgumentList(SyntaxFactory.BracketedArgumentList(
							SyntaxFactory.SingletonSeparatedList(
								SyntaxFactory.Argument(
									SyntaxFactoryHelpers.CreateIntLiteral(p.DefaultIndex))))),
						SyntaxFactory.MemberAccessExpression(
							SyntaxKind.SimpleMemberAccessExpression,
							SyntaxFactory.ParseTypeName(Consts.ComposableArgumentsDefaultState.FullName),
							SyntaxFactory.IdentifierName("ShouldUseDefault")));

					var assignment = SyntaxFactory.AssignmentExpression(
						SyntaxKind.SimpleAssignmentExpression,
						SyntaxFactory.IdentifierName(p.Name),
						SyntaxFactory.MemberAccessExpression(
							SyntaxKind.SimpleMemberAccessExpression,
							SyntaxFactory.ParseTypeName(providerTypeName),
							SyntaxFactory.IdentifierName("Value")));

					substStmts.Add(SyntaxFactory.IfStatement(
						condition,
						SyntaxFactory.Block(
							SyntaxFactory.SingletonList<StatementSyntax>(
								SyntaxFactory.ExpressionStatement(assignment).WithTrailingNewLine()))));
				}

				if (substStmts.Count > 0)
				{
					var origStmts = processedBody.Statements.ToArray();
					var newStmts = new StatementSyntax[substStmts.Count + origStmts.Length];
					substStmts.CopyTo(newStmts, 0);
					origStmts.CopyTo(newStmts, substStmts.Count);
					processedBody = processedBody.WithStatements(SyntaxFactory.List(newStmts));
				}
			}

			if (allStable && anyNormalParams)
			{
				var stateVarNames = new List<string>();
				foreach (var (param, index) in normalParams)
				{
					string stateVar = $"__{param.Name}_state";
					stateVarNames.Add(stateVar);

					if (param.DefaultProviderType != null)
					{
						var conditionalExpr = SyntaxFactory.ConditionalExpression(
							SyntaxFactory.BinaryExpression(
								SyntaxKind.EqualsExpression,
								SyntaxFactory.ElementAccessExpression(
									SyntaxFactory.IdentifierName(Consts.Rewriter.DefaultParamName))
								.WithArgumentList(SyntaxFactory.BracketedArgumentList(
									SyntaxFactory.SingletonSeparatedList(
										SyntaxFactory.Argument(
											SyntaxFactoryHelpers.CreateIntLiteral(param.DefaultIndex))))),
								SyntaxFactory.MemberAccessExpression(
									SyntaxKind.SimpleMemberAccessExpression,
									SyntaxFactory.ParseTypeName(Consts.ComposableArgumentsDefaultState.FullName),
									SyntaxFactory.IdentifierName("ShouldUseDefault"))),
							SyntaxFactory.MemberAccessExpression(
								SyntaxKind.SimpleMemberAccessExpression,
								SyntaxFactory.ParseTypeName(Consts.ComposableArgumentsState.FullName),
								SyntaxFactory.IdentifierName(Consts.ComposableArgumentsState.StaticField)),
							SyntaxFactory.ElementAccessExpression(
								SyntaxFactory.IdentifierName(changedVar))
							.WithArgumentList(SyntaxFactory.BracketedArgumentList(
								SyntaxFactory.SingletonSeparatedList(
									SyntaxFactory.Argument(
										SyntaxFactoryHelpers.CreateIntLiteral(index))))));

						tryStatements.Add(SyntaxFactory.LocalDeclarationStatement(
							SyntaxFactory.VariableDeclaration(
								SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.ByteKeyword)))
							.WithVariables(
								SyntaxFactory.SingletonSeparatedList(
									SyntaxFactory.VariableDeclarator(
										SyntaxFactory.Identifier(stateVar))
									.WithInitializer(SyntaxFactory.EqualsValueClause(conditionalExpr)))))
							.WithTrailingNewLine());
					}
					else
					{
						tryStatements.Add(SyntaxFactory.LocalDeclarationStatement(
							SyntaxFactory.VariableDeclaration(
								SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.ByteKeyword)))
							.WithVariables(
								SyntaxFactory.SingletonSeparatedList(
									SyntaxFactory.VariableDeclarator(
										SyntaxFactory.Identifier(stateVar))
									.WithInitializer(SyntaxFactory.EqualsValueClause(
										SyntaxFactory.ElementAccessExpression(
											SyntaxFactory.IdentifierName(changedVar))
										.WithArgumentList(SyntaxFactory.BracketedArgumentList(
											SyntaxFactory.SingletonSeparatedList(
												SyntaxFactory.Argument(
													SyntaxFactoryHelpers.CreateIntLiteral(index))))))))))
							.WithTrailingNewLine());
					}

					if (param.Type != null && param.Type.IsStableType())
					{
						tryStatements.Add(SyntaxFactory.IfStatement(
							SyntaxFactory.BinaryExpression(
								SyntaxKind.EqualsExpression,
								SyntaxFactory.IdentifierName(stateVar),
								SyntaxFactory.MemberAccessExpression(
									SyntaxKind.SimpleMemberAccessExpression,
									SyntaxFactory.ParseTypeName(Consts.ComposableArgumentsState.FullName),
									SyntaxFactory.IdentifierName(Consts.ComposableArgumentsState.UncertainField))),
							SyntaxFactory.Block(
								SyntaxFactory.SingletonList<StatementSyntax>(
									SyntaxFactory.ExpressionStatement(
										SyntaxFactory.AssignmentExpression(
											SyntaxKind.SimpleAssignmentExpression,
											SyntaxFactory.IdentifierName(stateVar),
											SyntaxFactory.ConditionalExpression(
												SyntaxFactory.InvocationExpression(
													SyntaxFactory.MemberAccessExpression(
														SyntaxKind.SimpleMemberAccessExpression,
														SyntaxFactory.IdentifierName(ctxVar),
														SyntaxFactory.IdentifierName(Consts.ComposeContext.ChangedMethod)))
												.WithArgumentList(SyntaxFactory.ArgumentList(
													SyntaxFactory.SingletonSeparatedList(
														SyntaxFactory.Argument(SyntaxFactory.IdentifierName(param.Name))))),
												SyntaxFactory.MemberAccessExpression(
													SyntaxKind.SimpleMemberAccessExpression,
													SyntaxFactory.ParseTypeName(Consts.ComposableArgumentsState.FullName),
													SyntaxFactory.IdentifierName(Consts.ComposableArgumentsState.DifferentField)),
												SyntaxFactory.MemberAccessExpression(
													SyntaxKind.SimpleMemberAccessExpression,
													SyntaxFactory.ParseTypeName(Consts.ComposableArgumentsState.FullName),
													SyntaxFactory.IdentifierName(Consts.ComposableArgumentsState.SameField)))))
									.WithTrailingNewLine()))));
					}
				}

				ExpressionSyntax? condition = null;
				foreach (var stateVar in stateVarNames)
				{
					var eqToSame = SyntaxFactory.BinaryExpression(
						SyntaxKind.EqualsExpression,
						SyntaxFactory.IdentifierName(stateVar),
						SyntaxFactory.MemberAccessExpression(
							SyntaxKind.SimpleMemberAccessExpression,
							SyntaxFactory.ParseTypeName(Consts.ComposableArgumentsState.FullName),
							SyntaxFactory.IdentifierName(Consts.ComposableArgumentsState.SameField)));

					var eqToStatic = SyntaxFactory.BinaryExpression(
						SyntaxKind.EqualsExpression,
						SyntaxFactory.IdentifierName(stateVar),
						SyntaxFactory.MemberAccessExpression(
							SyntaxKind.SimpleMemberAccessExpression,
							SyntaxFactory.ParseTypeName(Consts.ComposableArgumentsState.FullName),
							SyntaxFactory.IdentifierName(Consts.ComposableArgumentsState.StaticField)));

					var eq = SyntaxFactory.ParenthesizedExpression(
						SyntaxFactory.BinaryExpression(
							SyntaxKind.LogicalOrExpression,
							eqToSame,
							eqToStatic));

					condition = condition == null
						? eq
						: SyntaxFactory.BinaryExpression(SyntaxKind.LogicalAndExpression, condition, eq);
				}

				condition = SyntaxFactory.BinaryExpression(
					SyntaxKind.LogicalAndExpression,
					condition,
					SyntaxFactory.MemberAccessExpression(
						SyntaxKind.SimpleMemberAccessExpression,
						SyntaxFactory.IdentifierName(ctxVar),
						SyntaxFactory.IdentifierName(Consts.ComposeContext.SkippingProperty)));

				tryStatements.Add(SyntaxFactory.IfStatement(
					condition,
					SyntaxFactory.Block(
						SyntaxFactory.SingletonList<StatementSyntax>(
							SyntaxFactory.ExpressionStatement(
								SyntaxFactory.InvocationExpression(
									SyntaxFactory.MemberAccessExpression(
										SyntaxKind.SimpleMemberAccessExpression,
										SyntaxFactory.IdentifierName(ctxVar),
										SyntaxFactory.IdentifierName(Consts.ComposeContext.SkipToGroupEndMethod))))
								.WithTrailingNewLine())),
					SyntaxFactory.ElseClause(
						SyntaxFactory.Block(processedBody.Statements))));
			}
			else
			{
				tryStatements.AddRange(processedBody.Statements);
			}

			ExpressionStatementSyntax endGroupStatement = SyntaxFactoryHelpers.CreateSafeMethodCallOnVariableWithArgs(
				ctxVar,
				Consts.ComposeContext.EndRestartableGroupMethod,
				SyntaxFactoryHelpers.CreateIntLiteral(_session.InitialGroupId));

			syntaxList.Add(SyntaxFactory.TryStatement(
				SyntaxFactory.Block(tryStatements),
				default,
				SyntaxFactory.FinallyClause(SyntaxFactory.Block(SyntaxFactory.SingletonList<StatementSyntax>(endGroupStatement))))
				.WithTrailingNewLine());

			return node.WithStatements(SyntaxFactory.List(syntaxList));
		}

		public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
		{
			IMethodSymbol? methodSymbol = _semanticModel.GetSymbolInfo(node).Symbol as IMethodSymbol;
			if (methodSymbol == null)
				return base.VisitInvocationExpression(node);

			var context = new MethodCallHandlerContext(
				_semanticModel,
				_options,
				_methodCtx,
				_session,
				_session.Diagnostics,
				Visit);

			if (_wellKnownRegistry.TryHandle(methodSymbol, node, context, out var wellKnownReplacement))
				return wellKnownReplacement;

			foreach (var handler in _methodCallHandlers)
			{
				if (handler.TryHandle(node, methodSymbol, context, out var replacement))
				{
					return replacement;
				}
			}

			return base.VisitInvocationExpression(node);
		}

		public override SyntaxNode? VisitConditionalAccessExpression(ConditionalAccessExpressionSyntax node)
		{
			IMethodSymbol? methodSymbol = _semanticModel.GetSymbolInfo(node.WhenNotNull).Symbol as IMethodSymbol;
			if (methodSymbol == null)
				return base.VisitConditionalAccessExpression(node);

			var context = new MethodCallHandlerContext(
				_semanticModel,
				_options,
				_methodCtx,
				_session,
				_session.Diagnostics,
				Visit);

			foreach (var handler in _methodCallHandlers)
			{
				if (handler.TryHandle(node, methodSymbol, context, out var replacement))
				{
					return replacement;
				}
			}

			return base.VisitConditionalAccessExpression(node);
		}

		public override SyntaxNode VisitIfStatement(IfStatementSyntax node)
		{
			using var ifProcessingHanler = _session.EnterConditional();
			IEnumerable<StatementSyntax>? ifStatementsToProcesss = null;
			if (node.Statement is BlockSyntax blockSyntax)
			{
				ifStatementsToProcesss = blockSyntax.Statements;
			}
			else if (node.Statement is ExpressionStatementSyntax expressionStatementSyntax)
			{
				ifStatementsToProcesss = new StatementSyntax[] { expressionStatementSyntax };
			}
			if (ifStatementsToProcesss == null)
			{
				_session.Report(DiagnosticInfo.Create(
					DiagnosticDescriptors.DNC002_IfWithoutBlock,
					node.GetLocation()));
				return base.VisitIfStatement(node);
			}
			using ListPoolObject<StatementSyntax> ifOutStatements = ListPool<StatementSyntax>.Get();
			foreach (StatementSyntax statement in ifStatementsToProcesss)
			{
				StatementSyntax? newStatement = base.Visit(statement) as StatementSyntax;
				if (newStatement != null)
					ifOutStatements.Add(newStatement);
			}

			IEnumerable<StatementSyntax> elseStatementsToProcesss = null;
			bool isElseIfBlock = false;
			if (node.Else?.Statement is BlockSyntax elseBlockSyntax)
			{
				elseStatementsToProcesss = elseBlockSyntax.Statements;
			}
			else if (node.Else?.Statement is ExpressionStatementSyntax elseExpressionStatementSyntax)
			{
				elseStatementsToProcesss = new StatementSyntax[] { elseExpressionStatementSyntax };
			}
			else if (node.Else?.Statement is IfStatementSyntax innerIfStatements)
			{
				elseStatementsToProcesss = new StatementSyntax[] { innerIfStatements };
				isElseIfBlock = true;
			}

			using ListPoolObject<StatementSyntax> elseOutStatements = ListPool<StatementSyntax>.Get();
			if (elseStatementsToProcesss != null)
			{
				foreach (StatementSyntax statements in elseStatementsToProcesss)
				{
					StatementSyntax? newStatement = base.Visit(statements) as StatementSyntax;
					if (newStatement != null)
						elseOutStatements.Add(newStatement);
				}
			}

			if (!_session.WasInConditional)
				return base.VisitIfStatement(node);

			int ifGroupId = _session.NextGroupId();
			ElseClauseSyntax? newElseClauseSyntax = node.Else;
			if (elseOutStatements.Any())
			{
				int elseGroupId = _session.NextGroupId();
				ExpressionStatementSyntax elseGroupStartStatement = SyntaxFactoryHelpers.CreateSafeMethodCallOnVariableWithArgs(_options.ContextVarName,
					Consts.ComposeContext.StartReplaceableGroupMethod,
					SyntaxFactoryHelpers.CreateIntLiteral(elseGroupId))
					.WithTrailingNewLine();
				ExpressionStatementSyntax elseGroupEndStatement = SyntaxFactoryHelpers.CreateSafeMethodCallOnVariableWithArgs(_options.ContextVarName,
					Consts.ComposeContext.EndReplaceableGroupMethod,
					SyntaxFactoryHelpers.CreateIntLiteral(elseGroupId));

				if (isElseIfBlock)
				{
					StatementSyntax statementSyntax = default;
					if (elseOutStatements.Count == 1)
						statementSyntax = elseOutStatements[0];
					else
						statementSyntax = SyntaxFactory.Block(elseOutStatements);

					newElseClauseSyntax = node.Else
							.WithStatement(statementSyntax)
							.WithTrailingNewLine();
				}
				else
				{
					newElseClauseSyntax = node.Else.WithStatement(SyntaxFactory.Block(
							WrapStatementsWithGroupStartAndEndMethods(elseGroupStartStatement, elseOutStatements, elseGroupEndStatement)))
						.WithTrailingNewLine();
				}
			}
			IfStatementSyntax newIfStatement = node.WithElse(newElseClauseSyntax);

			if (ifOutStatements.Any())
			{
				ExpressionStatementSyntax ifGroupStartStatement = SyntaxFactoryHelpers.CreateSafeMethodCallOnVariableWithArgs(_options.ContextVarName,
						Consts.ComposeContext.StartReplaceableGroupMethod,
						SyntaxFactoryHelpers.CreateIntLiteral(ifGroupId))
					.WithTrailingNewLine();

				ExpressionStatementSyntax ifGroupEndStatement = SyntaxFactoryHelpers.CreateSafeMethodCallOnVariableWithArgs(_options.ContextVarName,
					Consts.ComposeContext.EndReplaceableGroupMethod,
					SyntaxFactoryHelpers.CreateIntLiteral(ifGroupId));

				newIfStatement = node.WithStatement(SyntaxFactory.Block(
						WrapStatementsWithGroupStartAndEndMethods(ifGroupStartStatement, ifOutStatements, ifGroupEndStatement)))
					.WithElse(newElseClauseSyntax)
					.WithTrailingNewLine();
			}

			return newIfStatement;
		}

		public override SyntaxNode VisitForStatement(ForStatementSyntax forStatement)
		{
			IEnumerable<StatementSyntax>? statementsToProcess = null;
			if (forStatement.Statement is BlockSyntax block)
			{
				statementsToProcess = block.Statements;
			}
			else
			{
				_session.Report(DiagnosticInfo.Create(
					DiagnosticDescriptors.DNC003_ForWithoutBlock,
					forStatement.GetLocation()));
				return base.VisitForStatement(forStatement);
			}
			if (statementsToProcess != null)
			{
				using ListPoolObject<StatementSyntax> outForStatements = ListPool<StatementSyntax>.Get();
				foreach (StatementSyntax processingStatement in statementsToProcess)
				{
					SyntaxNode st = base.Visit(processingStatement);

					if (st is StatementSyntax statement)
						outForStatements.Add(statement);
				}
				return forStatement.WithStatement(SyntaxFactory.Block(outForStatements)).WithTrailingNewLine();
			}
			else
			{
				return base.VisitForStatement(forStatement);
			}
		}

		public override SyntaxNode VisitForEachStatement(ForEachStatementSyntax forEachStatement)
		{
			IEnumerable<StatementSyntax> statementsToProcess = null;
			if (forEachStatement.Statement is BlockSyntax block)
			{
				statementsToProcess = block.Statements;
			}
			else
			{
				_session.Report(DiagnosticInfo.Create(
					DiagnosticDescriptors.DNC004_ForeachWithoutBlock,
					forEachStatement.GetLocation()));
				return base.VisitForEachStatement(forEachStatement);
			}
			if (statementsToProcess != null)
			{
				using ListPoolObject<StatementSyntax> outForEachStatements = ListPool<StatementSyntax>.Get();
				foreach (StatementSyntax processingStatement in statementsToProcess)
				{
					SyntaxNode st = base.Visit(processingStatement);

					if (st is StatementSyntax statement)
						outForEachStatements.Add(statement);
				}
				return forEachStatement.WithStatement(SyntaxFactory.Block(outForEachStatements)).WithTrailingNewLine();
			}
			else
			{
				return base.VisitForEachStatement(forEachStatement);
			}
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

		protected bool IsComposableAttributeSyntax(AttributeSyntax s)
		{
			var name = s.Name.ToString();
			return name == Consts.ComposableAttributeFullName ||
					name.EndsWith("Composable") ||
					name.EndsWith("ComposableAttribute");
		}

		protected SyntaxList<AttributeListSyntax> ReplaceComposableAttribute(SyntaxList<AttributeListSyntax> attributeLists)
		{
			return SyntaxFactory.List(attributeLists.Select(aList =>
			{
				IEnumerable<AttributeSyntax> newAttributes = aList.Attributes.Select(attribute =>
				{
					if (IsComposableAttributeSyntax(attribute))
						return SyntaxFactory.Attribute(SyntaxFactory.IdentifierName(Consts.ComposeGeneratedAttributeFullTypeName));
					else
						return attribute;
				});
				return aList.WithAttributes(SyntaxFactory.SeparatedList(newAttributes));
			}));
		}

		protected static IEnumerable<StatementSyntax> WrapStatementsWithGroupStartAndEndMethods(ExpressionStatementSyntax groupStart,
				  IList<StatementSyntax> statements,
				  ExpressionStatementSyntax groupEnd)
		{
			yield return groupStart;
			foreach (var statement in statements)
			{
				yield return statement;
			}
			yield return groupEnd;
		}
		protected ParameterListSyntax AppendComposableContextrelatedParameters(ParameterListSyntax paramList, string contextParamName, string changedParamName)
		{
			SeparatedSyntaxList<ParameterSyntax> newArguments = paramList.Parameters.AddRange(new ParameterSyntax[]
					{
				SyntaxFactory.Parameter(default,
					default,
					SyntaxFactory.ParseTypeName(Consts.ComposeContext.FullName).WithTrailingSpace(),
					SyntaxFactory.Identifier(contextParamName),
					default),

				SyntaxFactory.Parameter(default,
					default,
					SyntaxFactory.ParseTypeName(ComposableArgumentsState.FullName).WithTrailingSpace(),
					SyntaxFactory.Identifier(changedParamName),
					default),

				SyntaxFactory.Parameter(default,
					default,
					SyntaxFactory.ParseTypeName(Consts.ComposableArgumentsDefaultState.FullName).WithTrailingSpace(),
					SyntaxFactory.Identifier(Consts.Rewriter.DefaultParamName),
					default),
					});

			return paramList.WithParameters(newArguments);
		}

		protected ParameterListSyntax ReplaceAllComposableParameters(MethodDeclarationSyntax method, bool addAttributeToComposableParameters)
		{
			var args = method.ParameterList.Parameters.Zip(
					method.GetParametersInfos(_semanticModel),
					(parameter, paramInfo) => (parameter, paramInfo)
			);

			SeparatedSyntaxList<ParameterSyntax> newArguments = SyntaxFactory.SeparatedList(
				args.Select(s => (Syntax: s.parameter, ParamInfo: s.paramInfo))
						.Select(oldParam =>
						SyntaxFactory.Parameter(
							addAttributeToComposableParameters
								? (oldParam.ParamInfo.IsComposable
									? ReplaceComposableActionParameterAttributes(oldParam.Syntax.AttributeLists)
									: oldParam.Syntax.AttributeLists)
								: default,
							oldParam.Syntax.Modifiers,
							oldParam.ParamInfo.IsComposable
								? SyntaxFactory.ParseTypeName(Consts.ComposableAction.FullNameWithGenericArguments(oldParam.ParamInfo.GenericArguments.Select(t => t.GetFullMetadataName()))).WithTrailingSpace()
								: oldParam.Syntax.Type,
							oldParam.Syntax.Identifier,
							ReplaceDefaultArgumentValue(oldParam.Syntax.Default, oldParam.ParamInfo.IsComposable)
			)));

			return SyntaxFactory.ParameterList(newArguments);
		}


		private SyntaxList<AttributeListSyntax> ReplaceComposableActionParameterAttributes(SyntaxList<AttributeListSyntax> attributeLists)
		{
			return SyntaxFactory.List(attributeLists.Select(aList =>
			{
				IEnumerable<AttributeSyntax> newAttributes = aList.Attributes.Select(attribute =>
				{
					if (MethodDeclarationSyntaxExtensions.IsComposableAttribute(attribute, _semanticModel))
						return SyntaxFactory.Attribute(SyntaxFactory.IdentifierName(Consts.ComposableActionParameterFullTypeName));
					else
						return attribute;
				});
				return aList.WithAttributes(SyntaxFactory.SeparatedList(newAttributes));
			}));
		}

		private static EqualsValueClauseSyntax ReplaceDefaultArgumentValue(EqualsValueClauseSyntax defaultSyntax, bool isComposable)
		{
			if (defaultSyntax == null)
				return defaultSyntax;
			if (!isComposable)
				return defaultSyntax;

			return SyntaxFactory.EqualsValueClause(
							SyntaxFactory.LiteralExpression(
								SyntaxKind.DefaultLiteralExpression,
								SyntaxFactory.Token(SyntaxKind.DefaultKeyword)
							));
		}
	}
}
