using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis.Text;

namespace DotNetCompose.SourceGenerators.Rewriters
{
    internal sealed class DebugLineNumberSyntaxTreeWriter : CSharpSyntaxRewriter
    {
        private readonly bool _supportsEnhancedLineDirectives;
        private readonly SourceText _normalizedText;
        private readonly string _directiveIndentation;

        internal DebugLineNumberSyntaxTreeWriter(
            bool supportsEnhancedLineDirectives,
            SyntaxNode normalizedRoot)
        {
            _supportsEnhancedLineDirectives = supportsEnhancedLineDirectives;
            _normalizedText = normalizedRoot.GetText();
            SyntaxToken firstToken = normalizedRoot.GetFirstToken();
            LinePosition firstTokenPosition = _normalizedText.Lines.GetLinePosition(firstToken.SpanStart);
            TextLine firstLine = _normalizedText.Lines[firstTokenPosition.Line];
            _directiveIndentation = _normalizedText.ToString(
                TextSpan.FromBounds(firstLine.Start, firstToken.SpanStart));
        }

        public override SyntaxNode VisitLocalDeclarationStatement(LocalDeclarationStatementSyntax node)
        {
            var processed = base.VisitLocalDeclarationStatement(node);
            processed = WithSourceLineDirective(node.Declaration, processed);
            return processed;
        }
        public override SyntaxNode VisitExpressionStatement(ExpressionStatementSyntax node)
        {
            var processed = (ExpressionStatementSyntax)base.VisitExpressionStatement(node)!;
            if (RequiresAnchor(node) && HasLocation(node))
                return CreateAnchor(node, processed);

            return WithSourceLineDirective(node, processed);
        }

        public override SyntaxNode VisitIfStatement(IfStatementSyntax node) =>
            WithSourceLineDirective(node, base.VisitIfStatement(node));

        public override SyntaxNode VisitForStatement(ForStatementSyntax node) =>
            WithSourceLineDirective(node, base.VisitForStatement(node));

        public override SyntaxNode VisitForEachStatement(ForEachStatementSyntax node) =>
            WithSourceLineDirective(node, base.VisitForEachStatement(node));

        public override SyntaxNode VisitWhileStatement(WhileStatementSyntax node) =>
            WithSourceLineDirective(node, base.VisitWhileStatement(node));

        public override SyntaxNode VisitDoStatement(DoStatementSyntax node) =>
            WithSourceLineDirective(node, base.VisitDoStatement(node));

        public override SyntaxNode VisitSwitchStatement(SwitchStatementSyntax node) =>
            WithSourceLineDirective(node, base.VisitSwitchStatement(node));

        public override SyntaxNode VisitReturnStatement(ReturnStatementSyntax node) =>
            WithSourceLineDirective(node, base.VisitReturnStatement(node));

        public override SyntaxNode VisitThrowStatement(ThrowStatementSyntax node) =>
            WithSourceLineDirective(node, base.VisitThrowStatement(node));

        private static bool RequiresAnchor(ExpressionStatementSyntax node) =>
            node.DescendantNodesAndSelf().Any(item =>
                item.GetAnnotations("complex-composable-call").Any() ||
                item is LambdaExpressionSyntax || item is AnonymousMethodExpressionSyntax);

        private static bool HasLocation(SyntaxNode node) =>
            node.GetAnnotations("location").Any(annotation => annotation.Data != null);

        private SyntaxNode CreateAnchor(
            ExpressionStatementSyntax sourceNode,
            ExpressionStatementSyntax processed)
        {
            if (!TryGetLocation(sourceNode, out MappedLocation location))
                return processed;

            string directive = CreateDirective(location, sourceNode);
            SyntaxTriviaList sourceLeading = processed.GetLeadingTrivia();

            BlockSyntax anchor = (BlockSyntax)SyntaxFactory.ParseStatement("{ }");
            anchor = anchor.WithLeadingTrivia(
                SyntaxFactory.ParseLeadingTrivia(_directiveIndentation + directive + "\r\n")
                    .AddRange(sourceLeading));
            SyntaxTriviaList hiddenCloseLeading = SyntaxFactory.ParseLeadingTrivia(
                    "\r\n" + _directiveIndentation + "#line hidden\r\n")
                .AddRange(sourceLeading);
            anchor = anchor.WithCloseBraceToken(
                anchor.CloseBraceToken
                    .WithLeadingTrivia(hiddenCloseLeading)
                    .WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed));

            SyntaxTriviaList hiddenLeading = SyntaxFactory.ParseLeadingTrivia(
                    "#line hidden\r\n" + sourceLeading.ToFullString());
            processed = processed.WithLeadingTrivia(hiddenLeading);

            SyntaxToken openBrace = SyntaxFactory.Token(SyntaxKind.OpenBraceToken)
                .WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed);
            SyntaxToken closeBrace = SyntaxFactory.Token(SyntaxKind.CloseBraceToken)
                .WithLeadingTrivia(SyntaxFactory.ParseLeadingTrivia(
                    "\r\n" + sourceLeading.ToFullString()));

            BlockSyntax wrapper = SyntaxFactory.Block(
                openBrace,
                SyntaxFactory.List<StatementSyntax>(new StatementSyntax[] { anchor, processed }),
                closeBrace);
            return wrapper
                .WithLeadingTrivia(SyntaxFactory.ParseLeadingTrivia(
                    sourceLeading.ToFullString() + "#line hidden\r\n" + sourceLeading.ToFullString()))
                .WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed);
        }

        private SyntaxNode WithSourceLineDirective<T>(T node, SyntaxNode processed) where T : SyntaxNode
        {
            if (!TryGetLocation(node, out MappedLocation location))
                return processed;

            SyntaxTriviaList leading = SyntaxFactory.ParseLeadingTrivia(
                    _directiveIndentation + CreateDirective(location, node) + "\r\n")
                .AddRange(processed.GetLeadingTrivia());
            processed = processed.WithLeadingTrivia(leading);
            processed = AppendHiddenDirective(processed);

            return processed;
        }

        private SyntaxNode AppendHiddenDirective(SyntaxNode processed)
        {
            // NormalizeWhitespace can discard directives attached to a node's
            // trailing trivia in nested statement shapes. Keeping it on the last
            // token guarantees that generated scaffolding is hidden again.
            SyntaxToken lastToken = processed.GetLastToken();
            SyntaxTriviaList trailing = lastToken.TrailingTrivia;
            string lineBreak = trailing.Count > 0 && trailing.Last().IsKind(SyntaxKind.EndOfLineTrivia)
                ? string.Empty
                : "\r\n";
            trailing = trailing.AddRange(SyntaxFactory.ParseLeadingTrivia(
                lineBreak + _directiveIndentation + "#line hidden\r\n"));
            return processed.ReplaceToken(lastToken, lastToken.WithTrailingTrivia(trailing));
        }

        private string CreateDirective(MappedLocation location, SyntaxNode generatedNode)
        {
            string pathLiteral = SyntaxFactory.Literal(location.Path).ToFullString();
            int characterOffset = _normalizedText.Lines
                .GetLinePosition(generatedNode.GetFirstToken().SpanStart)
                .Character;
            return _supportsEnhancedLineDirectives
                ? $"#line ({location.StartLine}, {location.StartColumn}) - ({location.EndLine}, {location.EndColumn}) {characterOffset} {pathLiteral}"
                : $"#line {location.StartLine} {pathLiteral}";
        }

        private static bool TryGetLocation(SyntaxNode node, out MappedLocation location)
        {
            SyntaxAnnotation? annotation = node.GetAnnotations("location").FirstOrDefault();
            if (annotation?.Data == null)
            {
                location = default;
                return false;
            }

            string[] locations = annotation.Data.Split('|');
            if (locations.Length != 5)
            {
                location = default;
                return false;
            }

            location = new MappedLocation(
                int.Parse(locations[0]),
                int.Parse(locations[1]),
                int.Parse(locations[2]),
                int.Parse(locations[3]),
                Encoding.UTF8.GetString(Convert.FromBase64String(locations[4])));
            return true;
        }

        private readonly struct MappedLocation
        {
            public MappedLocation(int startLine, int startColumn, int endLine, int endColumn, string path)
            {
                StartLine = startLine;
                StartColumn = startColumn;
                EndLine = endLine;
                EndColumn = endColumn;
                Path = path;
            }

            public int StartLine { get; }
            public int StartColumn { get; }
            public int EndLine { get; }
            public int EndColumn { get; }
            public string Path { get; }
        }
    }
}
