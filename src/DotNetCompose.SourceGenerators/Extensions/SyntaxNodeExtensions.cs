using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Generic;
using System.Text;

namespace DotNetCompose.SourceGenerators.Extensions
{
    internal static class SyntaxNodeExtensions
    {
        /// <summary>
        /// Creates a new node from this node with the trailing trivia replaced.
        /// </summary>
        public static TSyntax WithTrailingNewLine<TSyntax>(
            this TSyntax node) where TSyntax : SyntaxNode
        {
            return node.WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed);
        }

    }
}
