using static DotNetCompose.SourceGenerators.Consts;

namespace DotNetCompose.SourceGenerators
{
    internal sealed record RewriterOptions(
        string ContextVarName,
        string ChangedVarName,
        string DefaultParamName,
        string StoredLambdaClassName,
        string BuildersClassName,
        ArgumentStateBufferStorage ArgumentStateBufferStorage = ArgumentStateBufferStorage.StackAlloc
    )
    {
        public static readonly RewriterOptions Default = new(
            Rewriter.ContextParamName,
            Rewriter.ChangedParamName,
            Rewriter.DefaultParamName,
            Rewriter.StoredLambdaClassName,
            Rewriter.BuildersClassName
        );
    }
}
