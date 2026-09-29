using DotNetCompose.SourceGenerators.Extensions;
using DotNetCompose.SourceGenerators.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Linq;

namespace DotNetCompose.SourceGenerators.Pipeline
{
    internal sealed class DefaultParameterChangedTransformer : IParameterChangedTransformer
    {
        public BlockSyntax TransformParameters(BlockSyntax body, TransformationContext context)
        {
            MethodGenerationContext methodCtx = context.MethodCtx;
            RewriterOptions options = context.Options;
            bool canSkip = methodCtx.CanSkip;
            bool generateDiagnostics = methodCtx.GenerateDiagnostics && methodCtx.GeneratesRestartGroup;
            List<(MethodDeclarationSyntaxExtensions.MethodParameterInfo Param, int Index)> trackedParams = methodCtx.Parameters
                .Select((parameter, index) => (Param: parameter, Index: index))
                .ToList();

            using ListPoolObject<StatementSyntax> prologueStatements = ListPool<StatementSyntax>.Get();
            using ListPoolObject<string> stateVariableNames = ListPool<string>.Get();
            string contextVariable = options.ContextVarName;
            string changedVariable = options.ChangedVarName;
            string? maskChangedVariable = null;
            HashSet<string> generatedNames = new HashSet<string>(
                body.DescendantTokens().Where(token => token.IsKind(SyntaxKind.IdentifierToken))
                    .Select(token => token.ValueText));
            foreach (var parameter in methodCtx.Parameters)
                generatedNames.Add(parameter.Name);

            if (canSkip && methodCtx.HasDefaultParams)
            {
                maskChangedVariable = AllocateName(generatedNames, Consts.Defaults.MaskChangedName);
                int defaultCount = methodCtx.Parameters.Count(parameter => parameter.DefaultProviderType != null);
                prologueStatements.Add(SyntaxFactory.LocalDeclarationStatement(
                    SyntaxFactory.VariableDeclaration(
                        SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.BoolKeyword)))
                    .WithVariables(SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.VariableDeclarator(SyntaxFactory.Identifier(maskChangedVariable))
                        .WithInitializer(SyntaxFactory.EqualsValueClause(
                            SyntaxFactoryHelpers.CreateMethodCallSyntaxWithArgs(
                                contextVariable, Consts.ComposeContext.ChangedDefaultMaskMethod,
                                SyntaxFactory.IdentifierName(options.DefaultParamName),
                                SyntaxFactoryHelpers.CreateIntLiteral(defaultCount))))))));
            }

            foreach ((MethodDeclarationSyntaxExtensions.MethodParameterInfo parameter, int index) in trackedParams)
            {
                string stateVariable = Consts.Rewriter.ParameterStateName(parameter.Name);
                stateVariableNames.Add(stateVariable);

                ExpressionSyntax initialState = parameter.DefaultProviderType != null
                    ? SyntaxFactory.ConditionalExpression(
                        SyntaxFactory.BinaryExpression(
                            SyntaxKind.EqualsExpression,
                            SyntaxFactory.ElementAccessExpression(
                                    SyntaxFactory.IdentifierName(Consts.Rewriter.DefaultParamName))
                                .WithArgumentList(SyntaxFactory.BracketedArgumentList(
                                    SyntaxFactory.SingletonSeparatedList(
                                        SyntaxFactory.Argument(
                                            SyntaxFactoryHelpers.CreateIntLiteral(parameter.DefaultIndex))))),
                            SyntaxFactory.MemberAccessExpression(
                                SyntaxKind.SimpleMemberAccessExpression,
                                SyntaxFactory.ParseTypeName(Consts.ComposableArgumentsDefaultState.FullName),
                                SyntaxFactory.IdentifierName(Consts.ComposableArgumentsDefaultState.ShouldUseDefaultField))),
                        SyntaxFactory.MemberAccessExpression(
                            SyntaxKind.SimpleMemberAccessExpression,
                            SyntaxFactory.ParseTypeName(Consts.ComposableArgumentsState.FullName),
                            SyntaxFactory.IdentifierName(Consts.ComposableArgumentsState.StaticField)),
                        CreateChangedStateAccess(changedVariable, index))
                    : CreateChangedStateAccess(changedVariable, index);

                prologueStatements.Add(
                    SyntaxFactory.LocalDeclarationStatement(
                            SyntaxFactory.VariableDeclaration(
                                    SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.ByteKeyword)))
                                .WithVariables(
                                    SyntaxFactory.SingletonSeparatedList(
                                        SyntaxFactory.VariableDeclarator(SyntaxFactory.Identifier(stateVariable))
                                            .WithInitializer(SyntaxFactory.EqualsValueClause(initialState)))))
                        .WithTrailingNewLine());

                if (canSkip)
                {
                    if (parameter.DefaultProviderType != null)
                    {
                        // The runtime consumes exactly one slot for every default parameter,
                        // but compares the value only when its forwarded state requires it.
                        prologueStatements.Add(SyntaxFactory.ExpressionStatement(
                            SyntaxFactory.AssignmentExpression(
                                SyntaxKind.SimpleAssignmentExpression,
                                SyntaxFactory.IdentifierName(stateVariable),
                                SyntaxFactoryHelpers.CreateMethodCallSyntaxWithArgs(
                                    contextVariable, Consts.ComposeContext.ResolveDefaultParameterStateMethod,
                                    SyntaxFactory.IdentifierName(parameter.Name),
                                    SyntaxFactory.IdentifierName(stateVariable)))));
                    }
                    else
                    {
                        prologueStatements.Add(CreateResolveUncertainStatement(
                            contextVariable,
                            stateVariable,
                            parameter.Name));
                    }
                }
            }

            DiagnosticsNames? diagnostics = null;
            bool diagnosticsEndLabelNeeded = false;
            BlockSyntax executionBody = body;
            if (generateDiagnostics)
            {
                HashSet<string> usedNames = new HashSet<string>(
                    body.DescendantTokens()
                        .Where(token => token.IsKind(SyntaxKind.IdentifierToken))
                        .Select(token => token.ValueText));
                foreach (MethodDeclarationSyntaxExtensions.MethodParameterInfo parameter in methodCtx.Parameters)
                    usedNames.Add(parameter.Name);
                foreach (string stateVariable in stateVariableNames)
                    usedNames.Add(stateVariable);

                diagnostics = new DiagnosticsNames(
                    AllocateName(usedNames, Consts.CompositionDiagnostics.TokenName),
                    AllocateName(usedNames, Consts.CompositionDiagnostics.OutcomeName),
                    AllocateName(usedNames, Consts.CompositionDiagnostics.EndLabelName));

                prologueStatements.AddRange(CreateDiagnosticsPrologue(context, diagnostics.Value));
                ReturnToDiagnosticsEndRewriter returnRewriter =
                    new ReturnToDiagnosticsEndRewriter(diagnostics.Value.EndLabel);
                executionBody = (BlockSyntax)returnRewriter.Visit(body)!;
                diagnosticsEndLabelNeeded = returnRewriter.RewroteReturn;
            }

            List<StatementSyntax> statements = new List<StatementSyntax>(prologueStatements.Count + 4);
            statements.AddRange(prologueStatements);

            if (canSkip)
            {
                List<StatementSyntax> skippedStatements = new List<StatementSyntax>();
                AddIfNotNull(skippedStatements, CreateOutcomeAssignment(
                    diagnostics,
                    Consts.CompositionDiagnostics.SkippedField));
                skippedStatements.Add(
                    SyntaxFactory.ExpressionStatement(
                            SyntaxFactory.InvocationExpression(
                                SyntaxFactory.MemberAccessExpression(
                                    SyntaxKind.SimpleMemberAccessExpression,
                                    SyntaxFactory.IdentifierName(contextVariable),
                                    SyntaxFactory.IdentifierName(Consts.ComposeContext.SkipToGroupEndMethod))))
                        .WithTrailingNewLine());

                List<StatementSyntax> executedStatements = new List<StatementSyntax>();
                AddIfNotNull(executedStatements, CreateOutcomeAssignment(
                    diagnostics,
                    Consts.CompositionDiagnostics.ExecutedField));
                executedStatements.AddRange(executionBody.Statements);

                statements.Add(SyntaxFactory.IfStatement(
                    CreateSkipCondition(contextVariable, changedVariable, stateVariableNames, maskChangedVariable),
                    SyntaxFactory.Block(skippedStatements),
                    SyntaxFactory.ElseClause(SyntaxFactory.Block(executedStatements))));
            }
            else
            {
                AddIfNotNull(statements, CreateOutcomeAssignment(
                    diagnostics,
                    Consts.CompositionDiagnostics.ExecutedField));
                statements.AddRange(executionBody.Statements);
            }

            if (diagnostics.HasValue)
                statements.AddRange(CreateDiagnosticsEpilogue(
                    context,
                    diagnostics.Value,
                    stateVariableNames,
                    diagnosticsEndLabelNeeded));

            return SyntaxFactory.Block(statements).WithTrailingNewLine();
        }

        private static void AddIfNotNull(List<StatementSyntax> statements, StatementSyntax? statement)
        {
            if (statement != null)
                statements.Add(statement);
        }

        private static ExpressionSyntax CreateChangedStateAccess(string changedVariable, int index)
        {
            return SyntaxFactory.ElementAccessExpression(SyntaxFactory.IdentifierName(changedVariable))
                .WithArgumentList(SyntaxFactory.BracketedArgumentList(
                    SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.Argument(SyntaxFactoryHelpers.CreateIntLiteral(index)))));
        }

        private static StatementSyntax CreateResolveUncertainStatement(
            string contextVariable,
            string stateVariable,
            string parameterName)
        {
            return SyntaxFactory.IfStatement(
                SyntaxFactory.BinaryExpression(
                    SyntaxKind.EqualsExpression,
                    SyntaxFactory.IdentifierName(stateVariable),
                    SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        SyntaxFactory.ParseTypeName(Consts.ComposableArgumentsState.FullName),
                        SyntaxFactory.IdentifierName(Consts.ComposableArgumentsState.UncertainField))),
                SyntaxFactory.Block(
                    SyntaxFactory.SingletonList<StatementSyntax>(
                        SyntaxFactory.ExpressionStatement(
                                SyntaxFactory.AssignmentExpression(
                                    SyntaxKind.SimpleAssignmentExpression,
                                    SyntaxFactory.IdentifierName(stateVariable),
                                    SyntaxFactory.ConditionalExpression(
                                        SyntaxFactory.InvocationExpression(
                                                SyntaxFactory.MemberAccessExpression(
                                                    SyntaxKind.SimpleMemberAccessExpression,
                                                    SyntaxFactory.IdentifierName(contextVariable),
                                                    SyntaxFactory.IdentifierName(Consts.ComposeContext.ChangedMethod)))
                                            .WithArgumentList(SyntaxFactory.ArgumentList(
                                                SyntaxFactory.SingletonSeparatedList(
                                                    SyntaxFactory.Argument(
                                                        SyntaxFactory.IdentifierName(parameterName))))),
                                        SyntaxFactory.MemberAccessExpression(
                                            SyntaxKind.SimpleMemberAccessExpression,
                                            SyntaxFactory.ParseTypeName(Consts.ComposableArgumentsState.FullName),
                                            SyntaxFactory.IdentifierName(Consts.ComposableArgumentsState.DifferentField)),
                                        SyntaxFactory.MemberAccessExpression(
                                            SyntaxKind.SimpleMemberAccessExpression,
                                            SyntaxFactory.ParseTypeName(Consts.ComposableArgumentsState.FullName),
                                            SyntaxFactory.IdentifierName(Consts.ComposableArgumentsState.SameField)))))
                            .WithTrailingNewLine())));
        }

        private static ExpressionSyntax CreateSkipCondition(
            string contextVariable,
            string changedVariable,
            IEnumerable<string> stateVariables,
            string? maskChangedVariable)
        {
            ExpressionSyntax? parameterCondition = null;
            foreach (string stateVariable in stateVariables)
            {
                ParenthesizedExpressionSyntax sameOrStatic = SyntaxFactory.ParenthesizedExpression(
                    SyntaxFactory.BinaryExpression(
                        SyntaxKind.LogicalOrExpression,
                        SyntaxFactory.BinaryExpression(
                            SyntaxKind.EqualsExpression,
                            SyntaxFactory.IdentifierName(stateVariable),
                            SyntaxFactory.MemberAccessExpression(
                                SyntaxKind.SimpleMemberAccessExpression,
                                SyntaxFactory.ParseTypeName(Consts.ComposableArgumentsState.FullName),
                                SyntaxFactory.IdentifierName(Consts.ComposableArgumentsState.SameField))),
                        SyntaxFactory.BinaryExpression(
                            SyntaxKind.EqualsExpression,
                            SyntaxFactory.IdentifierName(stateVariable),
                            SyntaxFactory.MemberAccessExpression(
                                SyntaxKind.SimpleMemberAccessExpression,
                                SyntaxFactory.ParseTypeName(Consts.ComposableArgumentsState.FullName),
                                SyntaxFactory.IdentifierName(Consts.ComposableArgumentsState.StaticField)))));

                parameterCondition = parameterCondition == null
                    ? sameOrStatic
                    : SyntaxFactory.BinaryExpression(
                        SyntaxKind.LogicalAndExpression,
                        parameterCondition,
                        sameOrStatic);
            }

            ExpressionSyntax notForced = SyntaxFactory.PrefixUnaryExpression(
                SyntaxKind.LogicalNotExpression,
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    SyntaxFactory.IdentifierName(changedVariable),
                    SyntaxFactory.IdentifierName(Consts.ComposableArgumentsState.IsForcedProperty)));
            ExpressionSyntax skipping = SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                SyntaxFactory.IdentifierName(contextVariable),
                SyntaxFactory.IdentifierName(Consts.ComposeContext.SkippingProperty));

            ExpressionSyntax condition = SyntaxFactory.BinaryExpression(
                SyntaxKind.LogicalAndExpression,
                notForced,
                SyntaxFactory.BinaryExpression(
                    SyntaxKind.LogicalAndExpression,
                    parameterCondition!,
                    skipping));
            return maskChangedVariable == null
                ? condition
                : SyntaxFactory.BinaryExpression(
                    SyntaxKind.LogicalAndExpression,
                    SyntaxFactory.PrefixUnaryExpression(
                        SyntaxKind.LogicalNotExpression,
                        SyntaxFactory.IdentifierName(maskChangedVariable)),
                    condition);
        }

        private static IEnumerable<StatementSyntax> CreateDiagnosticsPrologue(
            TransformationContext context,
            DiagnosticsNames names)
        {
            MethodGenerationContext method = context.MethodCtx;
            string parameterNames = string.Join("\u001f", method.Parameters.Select(parameter => parameter.Name));

            yield return SyntaxFactory.ParseStatement(
                $"{Consts.CompositionDiagnostics.TokenFullName} {names.Token} = default;");

            InvocationExpressionSyntax begin = SyntaxFactory.InvocationExpression(
                    SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        SyntaxFactory.ParseName(Consts.CompositionDiagnostics.RuntimeFullName),
                        SyntaxFactory.IdentifierName(Consts.CompositionDiagnostics.BeginMethod)))
                .WithArgumentList(SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(new[]
                {
                    SyntaxFactory.Argument(SyntaxFactory.LiteralExpression(
                        SyntaxKind.NumericLiteralExpression,
                        SyntaxFactory.Literal(method.DiagnosticsMethodId))),
                    SyntaxFactory.Argument(SyntaxFactoryHelpers.CreateIntLiteral(context.Session.InitialGroupId)),
                    CreateStringArgument(method.MethodName),
                    CreateStringArgument(method.SourcePath),
                    SyntaxFactory.Argument(SyntaxFactoryHelpers.CreateIntLiteral(method.SourceLine)),
                    CreateStringArgument(parameterNames)
                })));

            yield return SyntaxFactory.IfStatement(
                CreateIsSupportedAccess(),
                SyntaxFactory.Block(
                    SyntaxFactory.ExpressionStatement(
                        SyntaxFactory.AssignmentExpression(
                            SyntaxKind.SimpleAssignmentExpression,
                            SyntaxFactory.IdentifierName(names.Token),
                            begin))));

            yield return SyntaxFactory.ParseStatement(
                $"{Consts.CompositionDiagnostics.OutcomeFullName} {names.Outcome};");
        }

        private static IEnumerable<StatementSyntax> CreateDiagnosticsEpilogue(
            TransformationContext context,
            DiagnosticsNames names,
            IEnumerable<string> stateVariables,
            bool emitEndLabel)
        {
            string[] states = stateVariables.ToArray();
            ExpressionSyntax stateSpan = states.Length == 0
                ? SyntaxFactory.ParseExpression("stackalloc byte[0]")
                : SyntaxFactory.ParseExpression($"stackalloc byte[] {{ {string.Join(", ", states)} }}");

            InvocationExpressionSyntax end = SyntaxFactory.InvocationExpression(
                    SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        SyntaxFactory.ParseName(Consts.CompositionDiagnostics.RuntimeFullName),
                        SyntaxFactory.IdentifierName(Consts.CompositionDiagnostics.EndMethod)))
                .WithArgumentList(SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(new[]
                {
                    SyntaxFactory.Argument(SyntaxFactory.IdentifierName(names.Token)),
                    SyntaxFactory.Argument(SyntaxFactory.IdentifierName(names.Outcome)),
                    SyntaxFactory.Argument(SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        SyntaxFactory.IdentifierName(context.Options.ChangedVarName),
                        SyntaxFactory.IdentifierName(Consts.ComposableArgumentsState.IsForcedProperty))),
                    SyntaxFactory.Argument(stateSpan)
                })));

            IfStatementSyntax endIf = SyntaxFactory.IfStatement(
                SyntaxFactory.BinaryExpression(
                    SyntaxKind.LogicalAndExpression,
                    CreateIsSupportedAccess(),
                    SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        SyntaxFactory.IdentifierName(names.Token),
                        SyntaxFactory.IdentifierName(Consts.CompositionDiagnostics.IsActiveProperty))),
                SyntaxFactory.Block(SyntaxFactory.ExpressionStatement(end)));

            if (emitEndLabel)
            {
                yield return SyntaxFactory.LabeledStatement(
                    SyntaxFactory.Identifier(names.EndLabel),
                    SyntaxFactory.EmptyStatement());
            }
            yield return endIf.WithTrailingNewLine();
        }

        private static StatementSyntax? CreateOutcomeAssignment(
            DiagnosticsNames? diagnostics,
            string outcome)
        {
            if (!diagnostics.HasValue)
                return null;

            return SyntaxFactory.ExpressionStatement(
                    SyntaxFactory.AssignmentExpression(
                        SyntaxKind.SimpleAssignmentExpression,
                        SyntaxFactory.IdentifierName(diagnostics.Value.Outcome),
                        SyntaxFactory.MemberAccessExpression(
                            SyntaxKind.SimpleMemberAccessExpression,
                            SyntaxFactory.ParseTypeName(Consts.CompositionDiagnostics.OutcomeFullName),
                            SyntaxFactory.IdentifierName(outcome))))
                .WithTrailingNewLine();
        }

        private static ExpressionSyntax CreateIsSupportedAccess()
        {
            return SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                SyntaxFactory.ParseName(Consts.CompositionDiagnostics.RuntimeFullName),
                SyntaxFactory.IdentifierName(Consts.CompositionDiagnostics.IsSupportedProperty));
        }

        private static ArgumentSyntax CreateStringArgument(string value)
        {
            return SyntaxFactory.Argument(
                SyntaxFactory.LiteralExpression(
                    SyntaxKind.StringLiteralExpression,
                    SyntaxFactory.Literal(value)));
        }

        private static string AllocateName(HashSet<string> usedNames, string baseName)
        {
            string candidate = baseName;
            int suffix = 0;
            while (!usedNames.Add(candidate))
                candidate = $"{baseName}_{++suffix}";
            return candidate;
        }

        private readonly struct DiagnosticsNames
        {
            public DiagnosticsNames(string token, string outcome, string endLabel)
            {
                Token = token;
                Outcome = outcome;
                EndLabel = endLabel;
            }

            public string Token { get; }
            public string Outcome { get; }
            public string EndLabel { get; }
        }

        private sealed class ReturnToDiagnosticsEndRewriter : CSharpSyntaxRewriter
        {
            private readonly string _label;
            private readonly List<StatementSyntax> _activeGroupEnds = new List<StatementSyntax>();

            public ReturnToDiagnosticsEndRewriter(string label)
            {
                _label = label;
            }

            public bool RewroteReturn { get; private set; }

            public override SyntaxNode? VisitBlock(BlockSyntax node)
            {
                bool closesReplaceableGroup = node.Statements.Count >= 2 &&
                    IsComposerCall(node.Statements[0], Consts.ComposeContext.StartReplaceableGroupMethod) &&
                    IsComposerCall(node.Statements[node.Statements.Count - 1], Consts.ComposeContext.EndReplaceableGroupMethod);
                if (!closesReplaceableGroup)
                    return base.VisitBlock(node);

                _activeGroupEnds.Add(node.Statements[node.Statements.Count - 1]);
                try
                {
                    return base.VisitBlock(node);
                }
                finally
                {
                    _activeGroupEnds.RemoveAt(_activeGroupEnds.Count - 1);
                }
            }

            public override SyntaxNode? VisitReturnStatement(ReturnStatementSyntax node)
            {
                if (node.Expression != null)
                    return node;

                RewroteReturn = true;

                GotoStatementSyntax jump = SyntaxFactory.GotoStatement(
                        SyntaxKind.GotoStatement,
                        SyntaxFactory.IdentifierName(_label))
                    .WithTriviaFrom(node);
                jump = node.CopyAnnotationsTo(jump);
                if (_activeGroupEnds.Count == 0)
                    return jump;

                List<StatementSyntax> statements = new List<StatementSyntax>(_activeGroupEnds.Count + 1);
                for (int index = _activeGroupEnds.Count - 1; index >= 0; index--)
                    statements.Add(_activeGroupEnds[index].WithoutTrivia());
                statements.Add(jump);
                return SyntaxFactory.Block(statements);
            }

            public override SyntaxNode? VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node) => node;
            public override SyntaxNode? VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node) => node;
            public override SyntaxNode? VisitAnonymousMethodExpression(AnonymousMethodExpressionSyntax node) => node;
            public override SyntaxNode? VisitLocalFunctionStatement(LocalFunctionStatementSyntax node) => node;

            private static bool IsComposerCall(StatementSyntax statement, string methodName)
            {
                return statement is ExpressionStatementSyntax expressionStatement &&
                    expressionStatement.Expression is InvocationExpressionSyntax invocation &&
                    invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
                    memberAccess.Name.Identifier.ValueText == methodName;
            }
        }
    }
}
