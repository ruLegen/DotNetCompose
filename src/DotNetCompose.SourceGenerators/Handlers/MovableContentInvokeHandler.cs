using DotNetCompose.SourceGenerators.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Linq;
using static DotNetCompose.SourceGenerators.Consts;

namespace DotNetCompose.SourceGenerators.Handlers
{
    internal sealed class MovableContentInvokeHandler : IMethodCallHandler
    {
        private const string MovableContentTypeName = "DotNetCompose.Runtime.Composer.MovableContent`1";

        public bool TryHandle(
            ExpressionSyntax expression,
            IMethodSymbol methodSymbol,
            MethodCallHandlerContext context,
            out SyntaxNode? replacement)
        {
            replacement = null;

            if (methodSymbol.MethodKind != MethodKind.Ordinary)
                return false;
            if (methodSymbol.Name != "Invoke")
                return false;

            var containingType = methodSymbol.ContainingType;
            if (containingType == null)
                return false;

            if (containingType.OriginalDefinition?.GetFullMetadataName() != MovableContentTypeName)
                return false;

            if (expression is not InvocationExpressionSyntax invocationExpression)
                return false;

            replacement = Handle(invocationExpression, methodSymbol, context);
            return true;
        }

        private SyntaxNode Handle(
            InvocationExpressionSyntax invocation,
            IMethodSymbol methodSymbol,
            MethodCallHandlerContext context)
        {
            var options = context.Options;

            // Extract receiver (content) from expression: content.Invoke(...)
            if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
                return invocation;

            var receiver = memberAccess.Expression;

            // Get type arguments from the containing type (MovableContent<T>)
            var typeArgs = methodSymbol.ContainingType.TypeArguments;

            // Build target: global::Composables.Builders.InsertMovableContent<T>
            var target = SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                SyntaxFactory.ParseName("global::DotNetCompose.Runtime.Composables.Builders"),
                SyntaxFactory.IdentifierName("InsertMovableContent"));

            // If we have type arguments, attach them (but InsertMovableContent<T> uses the same T)
            if (typeArgs.Length > 0)
            {
                target = SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    SyntaxFactory.ParseName("global::DotNetCompose.Runtime.Composables.Builders"),
                    SyntaxFactory.GenericName("InsertMovableContent")
                        .WithTypeArgumentList(SyntaxFactory.TypeArgumentList(
                            SyntaxFactory.SeparatedList(typeArgs.Select(t =>
                                SyntaxFactory.ParseTypeName(t.ToDisplayString(
                                    SymbolDisplayFormat.FullyQualifiedFormat
                                        .WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Included))))))));
            }

            // Build argument list: content, param, ctx, changed, defaultState
            var allArgs = new List<ArgumentSyntax>
            {
                SyntaxFactory.Argument(receiver),  // the MovableContent instance
            };

            // Copy original arguments (param)
            allArgs.AddRange(invocation.ArgumentList.Arguments);

            // Add SG params
            allArgs.Add(SyntaxFactory.Argument(SyntaxFactory.IdentifierName(options.ContextVarName)));
            allArgs.Add(SyntaxFactory.Argument(SyntaxFactory.IdentifierName(options.ChangedVarName)));
            allArgs.Add(SyntaxFactory.Argument(SyntaxFactory.LiteralExpression(SyntaxKind.DefaultLiteralExpression)));

            return invocation
                .WithExpression(target)
                .WithArgumentList(SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(allArgs)));
        }
    }
}
