using System;

namespace DotNetCompose.Runtime
{
    public enum ComposableMode
    {
        Restartable = 0,
        ReadOnly,
        Inline,
        ReadOnlyInline,
        NonSkippable,
        NonRestartable,
        ExplicitGroups,
    }

    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Parameter, AllowMultiple = false)]
    public sealed class ComposableAttribute : Attribute
    {
        public ComposableAttribute()
            : this(ComposableMode.Restartable)
        {
        }

        public ComposableAttribute(ComposableMode mode)
        {
            Mode = mode;
        }

        public ComposableMode Mode { get; }
    }
}
