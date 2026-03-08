using DotNetCompose.SourceGenerators.Extensions;
using DotNetCompose.SourceGenerators.Helpers;
using System;
using System.Collections.Immutable;
using System.Threading;
using static DotNetCompose.SourceGenerators.Extensions.MethodDeclarationSyntaxExtensions;

namespace DotNetCompose.SourceGenerators
{
    internal class ComposableMethodGeneratorContext
    {
        private static int GlobalInitialGroupId = 2000;
        public ComposableMethodGeneratorContext()
        {
            _initialGroupId = Interlocked.Increment(ref GlobalInitialGroupId);
            _currentGroupId = _initialGroupId;
            _contextVarName = "__ctx";
            _changedVarName = "__changed";
        }
        public int InitialGroupId => _initialGroupId;
        public string ContextVarName => _contextVarName;
        public string ChangedVarName => _changedVarName;

        public bool WasGeneratedComposableFunctionWithinConditionalBlocks { get; private set; }
        public ImmutableArray<MethodParameterInfo> MethodParameters { get; internal set; } = ImmutableArray<MethodParameterInfo>.Empty;

        private int _initialGroupId = 0;
        private int _currentGroupId;
        private int _conditionalProccessingDepth;
        private readonly string _contextVarName;
        private readonly string _changedVarName;

        public int GetNextGroupId()
        {
            return Interlocked.Increment(ref _currentGroupId);
        }

        internal void StartIfProcessing()
        {
            _conditionalProccessingDepth++;
            // we just entered to Conditional block first time
            bool shouldResetFlag = _conditionalProccessingDepth == 1;
            if(shouldResetFlag)
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
            return DateTime.Now.GetHashCode();
        }
    }
}