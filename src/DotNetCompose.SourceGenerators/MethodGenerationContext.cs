using DotNetCompose.SourceGenerators.Extensions;
using System.Collections.Immutable;
using System.Linq;
using static DotNetCompose.SourceGenerators.Extensions.MethodDeclarationSyntaxExtensions;

namespace DotNetCompose.SourceGenerators
{
    internal sealed record MethodGenerationContext(
        string MethodName,
        ImmutableArray<string> TypeParameterNames,
        ImmutableArray<MethodParameterInfo> Parameters,
        bool HasDefaultParams,
        ComposableModeKind Mode)
    {
        public bool IsReadOnly => Mode == ComposableModeKind.ReadOnly;
        public bool GeneratesParameterChanges => Mode == ComposableModeKind.Restartable;
        public bool HasUnstableParameters => Parameters.Any(parameter =>
            !parameter.IsComposable &&
            (parameter.Type == null || !parameter.Type.IsStableType()));
        public bool CanSkip => GeneratesParameterChanges &&
            Parameters.Length > 0 &&
            !HasUnstableParameters;
        public bool GeneratesRestartGroup => Mode is ComposableModeKind.Restartable or ComposableModeKind.NonSkippable;
        public bool GeneratesControlFlowGroups => Mode is not ComposableModeKind.ReadOnly and not ComposableModeKind.ExplicitGroups;
    }
}
