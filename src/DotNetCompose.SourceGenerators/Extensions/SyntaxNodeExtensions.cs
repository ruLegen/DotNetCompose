using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace DotNetCompose.SourceGenerators.Extensions
{
    internal static class SyntaxNodeExtensions
    {
        /// <summary>
        /// Creates a new node from this node with the trailing trivia replaced.
        /// </summary>
        public static TSyntax WithTrailingNewLine<TSyntax>(this TSyntax node) where TSyntax : SyntaxNode
        {
            return node.WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed);
        }
        public static TSyntax WithLeadingNewLine<TSyntax>(this TSyntax node) where TSyntax : SyntaxNode
        {
            return node.WithLeadingTrivia(SyntaxFactory.CarriageReturnLineFeed);
        }

        /// <summary>
        /// Creates a new node from this node with the trailing trivia replaced.
        /// </summary>
        public static TSyntax WithTrailingSpace<TSyntax>(this TSyntax node) where TSyntax : SyntaxNode
        {
            return node.WithTrailingTrivia(SyntaxFactory.Space);
        }
        public static TSyntax WithLeadingSpace<TSyntax>(this TSyntax node) where TSyntax : SyntaxNode
        {
            return node.WithLeadingTrivia(SyntaxFactory.Space);
        }

        public static SyntaxAnnotation? CreateLocationSyntaxAnnotation<TSyntax>(this TSyntax node) where TSyntax : SyntaxNode
        {
            Location oldLocation = node.GetLocation();
            if (oldLocation != Location.None)
            {
                var lineSpanned = oldLocation.GetMappedLineSpan();
                string lineDirectiveLocation = string.Format("{0} {1} {2} {3} {4}", lineSpanned.StartLinePosition.Line,
                    lineSpanned.StartLinePosition.Character,
                    lineSpanned.EndLinePosition.Line,
                    lineSpanned.EndLinePosition.Character,
                    lineSpanned.Path);
                return new SyntaxAnnotation("location", lineDirectiveLocation);
            }
            return null;
        }

    }
}
