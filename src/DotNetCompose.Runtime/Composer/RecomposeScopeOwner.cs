using DotNetCompose.Runtime.Snapshots;

namespace DotNetCompose.Runtime.Composer
{
    internal interface RecomposeScopeOwner
    {
        InvalidationResult Invalidate(RecomposeScopeImpl scope, object? instance);
        void RecomposeScopeReleased(RecomposeScopeImpl scope);
        void RecordReadOf(object value);
    }
}
