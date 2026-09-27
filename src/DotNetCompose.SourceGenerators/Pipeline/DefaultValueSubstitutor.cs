using DotNetCompose.SourceGenerators.Extensions;
using DotNetCompose.SourceGenerators.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Linq;

namespace DotNetCompose.SourceGenerators.Pipeline
{
    internal sealed class DefaultValueSubstitutor : IDefaultValueSubstitutor
    {
        public BlockSyntax SubstituteDefaults(BlockSyntax body, TransformationContext context)
        {
            MethodDeclarationSyntaxExtensions.MethodParameterInfo[] parameters = context.MethodCtx.Parameters
                .Where(parameter => parameter.DefaultProviderType != null)
                .ToArray();
            if (parameters.Length == 0)
                return body;

            StatementSyntax defaults = context.MethodCtx.GeneratesRestartGroup
                ? BuildCachedDefaults(body, context, parameters)
                : SyntaxFactory.Block(BuildProviders(context, parameters, body.SpanStart));
            return body.WithStatements(body.Statements.Insert(0, defaults));
        }

        private static BlockSyntax BuildCachedDefaults(
            BlockSyntax body,
            TransformationContext context,
            MethodDeclarationSyntaxExtensions.MethodParameterInfo[] parameters)
        {
            string composer = context.Options.ContextVarName;
            string mask = context.Options.DefaultParamName;
            HashSet<string> names = new HashSet<string>(
                body.DescendantTokens().Where(token => token.IsKind(SyntaxKind.IdentifierToken))
                    .Select(token => token.ValueText));
            foreach (var parameter in context.MethodCtx.Parameters)
                names.Add(parameter.Name);
            string cacheName = AllocateName(names, "__dncDefaultsCache");
            string matchesName = AllocateName(names, "__dncDefaultMaskMatches");
            int groupKey = context.Session.NextGroupId();

            StatementSyntax cacheDeclaration = Local(SyntaxFactory.IdentifierName("var"), cacheName,
                SyntaxFactory.BinaryExpression(
                    SyntaxKind.AsExpression,
                    ComposerCall(composer, "RememberedValue"),
                    SyntaxFactory.ParseTypeName("global::DotNetCompose.Runtime.ComposableDefaultsCache")));
            ExpressionSyntax cacheNotNull = SyntaxFactory.BinaryExpression(
                SyntaxKind.NotEqualsExpression,
                SyntaxFactory.IdentifierName(cacheName),
                SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression));
            StatementSyntax matchesDeclaration = Local(
                SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.BoolKeyword)), matchesName,
                SyntaxFactory.BinaryExpression(
                    SyntaxKind.LogicalAndExpression,
                    cacheNotNull,
                    Call(Member(SyntaxFactory.IdentifierName(cacheName), "Matches"),
                        SyntaxFactory.IdentifierName(mask))));

            List<StatementSyntax> cached = new List<StatementSyntax>
            {
                SyntaxFactory.ExpressionStatement(ComposerCall(composer, "SkipToGroupEnd"))
            };
            cached.AddRange(parameters.Select(parameter => RestoreParameter(parameter, mask, cacheName)));

            List<StatementSyntax> recompute = new List<StatementSyntax>
            {
                ResetExplicitStatesWhenMaskChanges(parameters, mask, matchesName)
            };
            recompute.AddRange(BuildProviders(context, parameters, body.SpanStart));
            recompute.Add(SyntaxFactory.ExpressionStatement(ComposerCall(composer, "UpdateRememberedValue",
                CreateCache(mask, parameters))));

            ExpressionSyntax cacheValid = SyntaxFactory.BinaryExpression(
                SyntaxKind.LogicalAndExpression,
                SyntaxFactory.IdentifierName(matchesName),
                SyntaxFactory.PrefixUnaryExpression(
                    SyntaxKind.LogicalNotExpression,
                    Member(SyntaxFactory.IdentifierName(composer), "DefaultsInvalid")));
            StatementSyntax chooseDefaults = SyntaxFactory.IfStatement(
                cacheValid,
                SyntaxFactory.Block(cached),
                SyntaxFactory.ElseClause(SyntaxFactory.Block(recompute)));

            return SyntaxFactory.Block(
                SyntaxFactory.ExpressionStatement(ComposerCall(composer, "StartDefaults",
                    SyntaxFactoryHelpers.CreateIntLiteral(groupKey))),
                SyntaxFactory.TryStatement(
                    SyntaxFactory.Block(cacheDeclaration, matchesDeclaration, chooseDefaults),
                    default,
                    SyntaxFactory.FinallyClause(SyntaxFactory.Block(
                        SyntaxFactory.ExpressionStatement(ComposerCall(composer, "EndDefaults",
                            SyntaxFactoryHelpers.CreateIntLiteral(groupKey)))))));
        }

        private static StatementSyntax RestoreParameter(
            MethodDeclarationSyntaxExtensions.MethodParameterInfo parameter,
            string mask,
            string cacheName)
        {
            TypeSyntax type = TypeOf(parameter.Type!);
            GenericNameSyntax get = SyntaxFactory.GenericName(
                SyntaxFactory.Identifier("Get"),
                SyntaxFactory.TypeArgumentList(SyntaxFactory.SingletonSeparatedList(type)));
            ExpressionSyntax cache = SyntaxFactory.PostfixUnaryExpression(
                SyntaxKind.SuppressNullableWarningExpression,
                SyntaxFactory.IdentifierName(cacheName));
            return SyntaxFactory.IfStatement(MaskIsOmitted(mask, parameter.DefaultIndex),
                SyntaxFactory.Block(
                    Assign(parameter.Name, Call(Member(cache, get),
                        SyntaxFactoryHelpers.CreateIntLiteral(parameter.DefaultIndex))),
                    Assign(StateName(parameter), State("Same"))));
        }

        private static StatementSyntax ResetExplicitStatesWhenMaskChanges(
            MethodDeclarationSyntaxExtensions.MethodParameterInfo[] parameters,
            string mask,
            string matchesName)
        {
            return SyntaxFactory.IfStatement(
                SyntaxFactory.PrefixUnaryExpression(
                    SyntaxKind.LogicalNotExpression,
                    SyntaxFactory.IdentifierName(matchesName)),
                SyntaxFactory.Block(parameters.Select(parameter =>
                    (StatementSyntax)SyntaxFactory.IfStatement(
                        SyntaxFactory.BinaryExpression(
                            SyntaxKind.NotEqualsExpression,
                            MaskBit(mask, parameter.DefaultIndex),
                            Member(SyntaxFactory.ParseTypeName(Consts.ComposableArgumentsDefaultState.FullName),
                                "ShouldUseDefault")),
                        Assign(StateName(parameter), State("Uncertain"))))));
        }

        private static IEnumerable<StatementSyntax> BuildProviders(
            TransformationContext context,
            MethodDeclarationSyntaxExtensions.MethodParameterInfo[] parameters,
            int position)
        {
            string composer = context.Options.ContextVarName;
            string mask = context.Options.DefaultParamName;
            foreach (var parameter in parameters)
            {
                StatementSyntax assign = Assign(parameter.Name, ProviderCall(parameter, context, position));
                List<StatementSyntax> statements = new List<StatementSyntax>();
                if (context.IsReadOnly)
                {
                    statements.Add(assign);
                }
                else
                {
                    int groupKey = context.Session.NextGroupId();
                    statements.Add(SyntaxFactory.ExpressionStatement(ComposerCall(composer,
                        "StartReplaceableGroup", SyntaxFactoryHelpers.CreateIntLiteral(groupKey))));
                    statements.Add(SyntaxFactory.TryStatement(
                        SyntaxFactory.Block(assign),
                        default,
                        SyntaxFactory.FinallyClause(SyntaxFactory.Block(
                            SyntaxFactory.ExpressionStatement(ComposerCall(composer,
                                "EndReplaceableGroup", SyntaxFactoryHelpers.CreateIntLiteral(groupKey)))))));
                }
                statements.Add(Assign(StateName(parameter), State("Uncertain")));
                yield return SyntaxFactory.IfStatement(
                    MaskIsOmitted(mask, parameter.DefaultIndex),
                    SyntaxFactory.Block(statements));
            }
        }

        private static ExpressionSyntax ProviderCall(
            MethodDeclarationSyntaxExtensions.MethodParameterInfo parameter,
            TransformationContext context,
            int position)
        {
            IMethodSymbol? method = DefaultProviderResolver.Resolve(
                parameter.DefaultProviderType!, parameter.Type!, context.SemanticModel,
                position, context.IsReadOnly);
            if (method == null)
                return SyntaxFactory.DefaultExpression(TypeOf(parameter.Type!));

            ExpressionSyntax provider = TypeOf(parameter.DefaultProviderType!);
            if (!method.IsComposableFunction())
                return Call(Member(provider, "Create"));
            return Call(Member(Member(provider, "Builders"), "Create"),
                SyntaxFactory.IdentifierName(context.Options.ContextVarName),
                SyntaxFactory.LiteralExpression(SyntaxKind.DefaultLiteralExpression),
                SyntaxFactory.LiteralExpression(SyntaxKind.DefaultLiteralExpression));
        }

        private static ExpressionSyntax CreateCache(
            string mask,
            MethodDeclarationSyntaxExtensions.MethodParameterInfo[] parameters)
        {
            ArrayTypeSyntax arrayType = SyntaxFactory.ArrayType(
                SyntaxFactory.NullableType(
                    SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.ObjectKeyword))),
                SyntaxFactory.SingletonList(SyntaxFactory.ArrayRankSpecifier(
                    SyntaxFactory.SingletonSeparatedList<ExpressionSyntax>(
                        SyntaxFactory.OmittedArraySizeExpression()))));
            ExpressionSyntax values = SyntaxFactory.ArrayCreationExpression(arrayType,
                SyntaxFactory.InitializerExpression(SyntaxKind.ArrayInitializerExpression,
                    SyntaxFactory.SeparatedList<ExpressionSyntax>(parameters.Select(parameter =>
                        SyntaxFactory.IdentifierName(parameter.Name)))));
            return SyntaxFactory.ObjectCreationExpression(
                    SyntaxFactory.ParseTypeName("global::DotNetCompose.Runtime.ComposableDefaultsCache"))
                .WithArgumentList(SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(new[]
                {
                    SyntaxFactory.Argument(SyntaxFactory.IdentifierName(mask)),
                    SyntaxFactory.Argument(values)
                })));
        }

        private static ExpressionSyntax MaskIsOmitted(string mask, int index) =>
            SyntaxFactory.BinaryExpression(
                SyntaxKind.EqualsExpression,
                MaskBit(mask, index),
                Member(SyntaxFactory.ParseTypeName(Consts.ComposableArgumentsDefaultState.FullName),
                    "ShouldUseDefault"));

        private static ElementAccessExpressionSyntax MaskBit(string mask, int index) =>
            SyntaxFactory.ElementAccessExpression(SyntaxFactory.IdentifierName(mask))
                .WithArgumentList(SyntaxFactory.BracketedArgumentList(
                    SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(
                        SyntaxFactoryHelpers.CreateIntLiteral(index)))));

        private static TypeSyntax TypeOf(ITypeSymbol symbol) =>
            SyntaxFactory.ParseTypeName(symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));

        private static ExpressionSyntax State(string name) =>
            Member(SyntaxFactory.ParseTypeName(Consts.ComposableArgumentsState.FullName), name);

        private static string StateName(MethodDeclarationSyntaxExtensions.MethodParameterInfo parameter) =>
            $"__{parameter.Name}_state";

        private static LocalDeclarationStatementSyntax Local(
            TypeSyntax type, string name, ExpressionSyntax value) =>
            SyntaxFactory.LocalDeclarationStatement(
                SyntaxFactory.VariableDeclaration(type)
                    .WithVariables(SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.VariableDeclarator(SyntaxFactory.Identifier(name))
                            .WithInitializer(SyntaxFactory.EqualsValueClause(value)))));

        private static ExpressionStatementSyntax Assign(string name, ExpressionSyntax value) =>
            SyntaxFactory.ExpressionStatement(SyntaxFactory.AssignmentExpression(
                SyntaxKind.SimpleAssignmentExpression,
                SyntaxFactory.IdentifierName(name), value));

        private static InvocationExpressionSyntax ComposerCall(
            string composer, string method, params ExpressionSyntax[] arguments) =>
            Call(Member(SyntaxFactory.IdentifierName(composer), method), arguments);

        private static InvocationExpressionSyntax Call(
            ExpressionSyntax target, params ExpressionSyntax[] arguments) =>
            SyntaxFactory.InvocationExpression(target).WithArgumentList(
                SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(
                    arguments.Select(SyntaxFactory.Argument))));

        private static MemberAccessExpressionSyntax Member(ExpressionSyntax receiver, string name) =>
            Member(receiver, SyntaxFactory.IdentifierName(name));

        private static MemberAccessExpressionSyntax Member(ExpressionSyntax receiver, SimpleNameSyntax name) =>
            SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, receiver, name);

        private static string AllocateName(HashSet<string> names, string baseName)
        {
            string candidate = baseName;
            int suffix = 0;
            while (!names.Add(candidate))
                candidate = $"{baseName}_{++suffix}";
            return candidate;
        }
    }
}
