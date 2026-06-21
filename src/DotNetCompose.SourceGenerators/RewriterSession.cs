using DotNetCompose.SourceGenerators.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace DotNetCompose.SourceGenerators
{
    internal sealed class RewriterSession
    {
        private int _currentGroupId;
        private int _nextLambdaKey;
        private int _conditionalDepth;

        public RewriterSession(int initialGroupId)
        {
            _currentGroupId = initialGroupId;
            InitialGroupId = initialGroupId;
        }

        public int InitialGroupId { get; }
        public List<StoredLambda> StoredLambdas { get; } = new();
        public bool WasInConditional { get; private set; }

        public int NextGroupId() => ++_currentGroupId;
        public int NextLambdaKey() => _nextLambdaKey++;
        public string NextLambdaName() => $"__Lambda_{(uint)NextLambdaKey()}";

        public IDisposable EnterConditional()
        {
            bool wasFirst = _conditionalDepth == 0;
            _conditionalDepth++;
            if (wasFirst) WasInConditional = false;
            return new ActionDisposable(() => ExitConditional());
        }

        private void ExitConditional()
        {
            _conditionalDepth--;
            if (_conditionalDepth < 0)
                throw new InvalidOperationException();
        }

        public void MarkComposableProcessed()
        {
            if (_conditionalDepth > 0)
                WasInConditional = true;
        }

        public void AddStoredLambda(StoredLambda lambda) => StoredLambdas.Add(lambda);

        public static int DeterministicHash(string str)
        {
            int hash1 = 5381;
            int hash2 = hash1;

            for (int i = 0; i < str.Length; i += 2)
            {
                hash1 = ((hash1 << 5) + hash1) ^ str[i];
                if (i + 1 >= str.Length)
                    break;
                hash2 = ((hash2 << 5) + hash2) ^ str[i + 1];
            }

            return hash1 + (hash2 * 1566083941);
        }

        public record StoredLambda(string Name, ImmutableArray<(string Type, string Name)> Parameters, CSharpSyntaxNode MethodDeclaration);
    }
}
