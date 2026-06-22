using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;

namespace DotNetCompose.SourceGenerators.Pipeline
{
    internal sealed class NodeTransformer 
    {
        public NodeTransformer(Func<SyntaxNode, SyntaxNode> transformer)
        {
            _transformer = transformer; 
        }
        private Func<SyntaxNode, SyntaxNode>? _transformer;

        public SyntaxNode Transform(SyntaxNode node)
            => _transformer?.Invoke(node) ?? node;
    }
}
