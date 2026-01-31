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
        }
        public int InitialGroupId => _initialGroupId;

        private int _initialGroupId = 0;
        private int _currentGroupId;

        public int GetNextGroupId()
        {
            return Interlocked.Increment(ref _currentGroupId);
        }
    }
}