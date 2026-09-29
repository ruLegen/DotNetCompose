namespace DotNetCompose.SourceGenerators.Pipeline
{
    internal sealed record StrategyContainer(
        ISignatureRewriter SignatureRewriter,
        IBodyWrappingStrategy BodyWrapping,
        IControlFlowStrategy ControlFlow,
        ICallInterceptionStrategy CallInterception,
        IDefaultValueSubstitutor DefaultValueSubstitutor,
        IParameterChangedTransformer StableParameterCheckTransformer
    )
    {
        internal static readonly StrategyContainer Default = new(
            new DefaultSignatureRewriter(),
            new DefaultBodyWrappingStrategy(),
            new DefaultControlFlowStrategy(),
            new DefaultCallInterceptionStrategy(),
            new DefaultValueSubstitutor(),
            new DefaultParameterChangedTransformer()
        );
    }
}
