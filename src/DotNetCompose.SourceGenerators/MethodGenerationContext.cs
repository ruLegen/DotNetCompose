using System.Collections.Immutable;
using static DotNetCompose.SourceGenerators.Extensions.MethodDeclarationSyntaxExtensions;

namespace DotNetCompose.SourceGenerators
{
    internal sealed record MethodGenerationContext(
        string MethodName,
        ImmutableArray<string> TypeParameterNames,
        ImmutableArray<MethodParameterInfo> Parameters,
        bool HasDefaultParams,
        bool HasUnstableParam
    );
}
