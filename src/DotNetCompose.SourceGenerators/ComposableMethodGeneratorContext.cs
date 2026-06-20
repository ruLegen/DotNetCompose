using DotNetCompose.SourceGenerators.Extensions;
using DotNetCompose.SourceGenerators.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using static DotNetCompose.SourceGenerators.Extensions.MethodDeclarationSyntaxExtensions;

namespace DotNetCompose.SourceGenerators
{
    internal class ComposableMethodGeneratorContext
    {
        public ComposableMethodGeneratorContext(string methodId, string? contextParamName = null, string? changedParamName = null, string? storedLambdaClassName = null, string? builderClassName = null)
        {
            _initialGroupId = GetDeterministicId(methodId);
            _currentGroupId = _initialGroupId;

            ContextVarName = contextParamName ?? Consts.Rewriter.ContextParamName;
            ChangedVarName = changedParamName ?? Consts.Rewriter.ChangedParamName;
            StoredLambdaIdentifierName = storedLambdaClassName ?? Consts.Rewriter.StoredLambdaClassName; 
            BuildersClassName = builderClassName ?? Consts.Rewriter.BuildersClassName;
        }
        public int InitialGroupId => _initialGroupId;
        public string ContextVarName { get; }
        public string ChangedVarName { get; }
        public string StoredLambdaIdentifierName { get; }
        public string BuildersClassName { get; }
        public List<StoredLambda> StoredLambdas { get; } = new List<StoredLambda> { };
        public bool WasGeneratedComposableFunctionWithinConditionalBlocks { get; private set; }
        public bool HasUnstableParam { get; set; }
        public ImmutableArray<MethodParameterInfo> MethodParameters { get; internal set; } = ImmutableArray<MethodParameterInfo>.Empty;

        private int _initialGroupId = 0;
        private int _currentGroupId;
        private int _conditionalProccessingDepth;
        private int _nextLambdaKey;

        public int GetNextGroupId()
        {
            return Interlocked.Increment(ref _currentGroupId);
        }

        internal void StartIfProcessing()
        {
            _conditionalProccessingDepth++;
            // we just entered to Conditional block first time
            bool shouldResetFlag = _conditionalProccessingDepth == 1;
            if (shouldResetFlag)
                WasGeneratedComposableFunctionWithinConditionalBlocks = false;
        }

        internal IDisposable WithIfProcessing()
        {
            StartIfProcessing();
            return new ActionDisposable(() => EndIfProcessing());
        }

        internal void EndIfProcessing()
        {
            _conditionalProccessingDepth--;
            if (_conditionalProccessingDepth < 0)
                throw new InvalidOperationException();
        }

        internal void ComposableProcessed()
        {
            if (_conditionalProccessingDepth > 0)
                WasGeneratedComposableFunctionWithinConditionalBlocks = true;
        }

        internal int GetNextLambdaKey()
        {
            return _nextLambdaKey++;
        }

        internal string GetNextLambdaName()
        {
            var key = (uint)GetNextLambdaKey();
            return "__Lambda_" + key;
        }

        private static int GetDeterministicId(string str)
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

        internal void AddStoredLambda(StoredLambda lambda)
        {
            StoredLambdas.Add(lambda);
        }

        public record StoredLambda(string Name, ImmutableArray<(string Type, string Name)> Parameters, CSharpSyntaxNode MethodDeclaration);
    }
}