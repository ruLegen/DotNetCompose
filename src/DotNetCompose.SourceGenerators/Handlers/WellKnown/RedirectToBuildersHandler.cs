using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using static DotNetCompose.SourceGenerators.Consts;

namespace DotNetCompose.SourceGenerators.Handlers.WellKnown
{
    internal abstract class RedirectToBuildersHandler : WellKnownHandler
    {
        public override SyntaxNode Handle(
            InvocationExpressionSyntax invocation,
            MethodCallHandlerContext context)
        {
            var options = context.Options;
            var methodSymbol = context.SemanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
            if (methodSymbol == null)
                return null;

            var args = new List<ArgumentSyntax>();
            args.AddRange(invocation.ArgumentList.Arguments);
            args.Add(SyntaxFactory.Argument(SyntaxFactory.IdentifierName(options.ContextVarName)));
            args.Add(SyntaxFactory.Argument(SyntaxFactory.IdentifierName(options.ChangedVarName)));
            args.Add(SyntaxFactory.Argument(SyntaxFactory.IdentifierName(Rewriter.DefaultParamName)));

            var methodName = methodSymbol.TypeArguments.Length > 0
                ? (SimpleNameSyntax)SyntaxFactory.GenericName(methodSymbol.Name)
                    .WithTypeArgumentList(SyntaxFactory.TypeArgumentList(
                        SyntaxFactory.SeparatedList(
                            methodSymbol.TypeArguments.Select(t =>
                                SyntaxFactory.ParseTypeName(t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))))))
                : SyntaxFactory.IdentifierName(methodSymbol.Name);

            var target = SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                SyntaxFactory.ParseName("global::DotNetCompose.Runtime.Composables.Builders"),
                methodName);

            return invocation
                .WithExpression(target)
                .WithArgumentList(SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(args)));
        }
    }

    //internal sealed class RememberHandler : RedirectToBuildersHandler
    //{
    //    public override ImmutableArray<string> GetMetadataNames()
    //        => ImmutableArray.Create("DotNetCompose.Runtime.Composables.Remember");
    //}

    //internal sealed class ComposeNodeHandler : RedirectToBuildersHandler
    //{
    //    public override ImmutableArray<string> GetMetadataNames()
    //        => ImmutableArray.Create("DotNetCompose.Runtime.Composables.ComposeNode");
    //}
}
