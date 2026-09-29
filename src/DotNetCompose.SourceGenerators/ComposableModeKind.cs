namespace DotNetCompose.SourceGenerators
{
    public enum ComposableModeKind
    {
        Restartable = 0,
        ReadOnly,
        Inline,
        ReadOnlyInline,
        NonSkippable,
        NonRestartable,
        ExplicitGroups,
    }

    internal static class ComposableModeKindExtensions
    {
        public static bool IsValid(this ComposableModeKind mode)
            => mode is >= ComposableModeKind.Restartable and <= ComposableModeKind.ExplicitGroups;

        public static bool IsValidMethodMode(this ComposableModeKind mode)
            => mode is ComposableModeKind.Restartable
                or ComposableModeKind.ReadOnly
                or ComposableModeKind.Inline
                or ComposableModeKind.NonSkippable
                or ComposableModeKind.NonRestartable
                or ComposableModeKind.ExplicitGroups;

        public static bool IsValidParameterMode(this ComposableModeKind mode)
            => mode is ComposableModeKind.Restartable
                or ComposableModeKind.ReadOnly
                or ComposableModeKind.Inline
                or ComposableModeKind.ReadOnlyInline;

        public static bool IsReadOnly(this ComposableModeKind mode)
            => mode is ComposableModeKind.ReadOnly or ComposableModeKind.ReadOnlyInline;

        public static bool IsInline(this ComposableModeKind mode)
            => mode is ComposableModeKind.Inline or ComposableModeKind.ReadOnlyInline;

        public static ComposableModeKind EffectiveParameterMode(
            this ComposableModeKind parameterMode,
            ComposableModeKind methodMode)
        {
            if (methodMode != ComposableModeKind.Inline)
                return parameterMode;

            return parameterMode switch
            {
                ComposableModeKind.Restartable => ComposableModeKind.Inline,
                ComposableModeKind.ReadOnly => ComposableModeKind.ReadOnlyInline,
                _ => parameterMode,
            };
        }
    }
}
