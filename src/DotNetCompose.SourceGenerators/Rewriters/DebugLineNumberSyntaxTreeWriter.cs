using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DotNetCompose.SourceGenerators.Rewriters
{
    internal class DebugLineNumberSyntaxTreeWriter : CSharpSyntaxRewriter
    {
        public override SyntaxNode VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            var processed = base.VisitMethodDeclaration(node);
            processed = WithSourceLineDirective(node, processed);
            return processed;
        }
        public override SyntaxNode VisitVariableDeclaration(VariableDeclarationSyntax node)
        {
            var processed = base.VisitVariableDeclaration(node);
            processed = WithSourceLineDirective(node, processed);
            return processed;
        }
        public override SyntaxNode VisitExpressionStatement(ExpressionStatementSyntax node)
        {
            var processed = base.VisitExpressionStatement(node);
            processed = WithSourceLineDirective(node, processed);
            return processed;
        }

        private static SyntaxNode WithSourceLineDirective<T>(T node, SyntaxNode processed) where T : SyntaxNode 
        {
            var location = node.GetAnnotations("location");
            if (location.Any())
            {
                var locations = location.First().Data.Split(' ');
                LineSpanDirectiveTriviaSyntax lineDirective = SyntaxFactory.LineSpanDirectiveTrivia(
                    SyntaxFactory.LineDirectivePosition(SyntaxFactory.Literal(int.Parse(locations[0])), SyntaxFactory.Literal(int.Parse(locations[1]))),
                    SyntaxFactory.LineDirectivePosition(SyntaxFactory.Literal(int.Parse(locations[2])), SyntaxFactory.Literal(int.Parse(locations[3]))),
                    SyntaxFactory.Literal(locations[4]),
                    false // Is hidden
                );

                processed = processed.WithLeadingTrivia(
                    SyntaxFactory.Trivia(lineDirective),
                    SyntaxFactory.CarriageReturnLineFeed
                );

                //SyntaxFactory.Token(SyntaxKind.DefaultKeyword)
                processed = processed.WithTrailingTrivia(
                    SyntaxFactory.Trivia(lineDirective.WithIsActive(false)),
                    SyntaxFactory.CarriageReturnLineFeed
                );

            }

            return processed;
        }
    }
}
