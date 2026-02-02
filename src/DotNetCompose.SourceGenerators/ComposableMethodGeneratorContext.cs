using System;
using System.Threading;

namespace DotNetCompose.SourceGenerators
{
    internal class ComposableMethodGeneratorContext
    {
        private static int GlobalInitialGroupId = 2000;
        public ComposableMethodGeneratorContext()
        {
            _initialGroupId = Interlocked.Increment(ref GlobalInitialGroupId);
            _currentGroupId = _initialGroupId;
            _contextVarName = "var0";
        }
        public int InitialGroupId => _initialGroupId;
        public string ContextVarName => _contextVarName;

        public bool WasGeneratedComposableFunctionWithinConditionalBlocks { get; private set; }

        private int _initialGroupId = 0;
        private int _currentGroupId;
        private int _conditionalProccessingDepth;
        private readonly string _contextVarName;

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
    }
}