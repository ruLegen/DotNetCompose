using System;
using DotNetCompose.Runtime.Composer;

namespace DotNetCompose.Runtime
{
    public static partial class Composables
    {
        private const int SideEffectKeyGroup = -19001;
        private const int SideEffectKeysGroup = -19002;

        /// <summary>Runs after a successful apply and remember callbacks, whenever this call executes.</summary>
        [Composable(ComposableMode.NonRestartable)]
        public static void SideEffect(Action effect)
        {
            if (effect == null) { throw new ArgumentNullException(nameof(effect)); }
            IComposerContext context = CurrentContext()
                ?? throw new InvalidOperationException("SideEffect requires a composition.");
            context.RecordSideEffect(effect);
        }

        /// <summary>Runs after a successful apply on entry and whenever the key changes.</summary>
        [Composable(ComposableMode.NonRestartable)]
        public static void SideEffect(object? key, Action effect)
        {
            if (effect == null) { throw new ArgumentNullException(nameof(effect)); }
            IComposerContext context = CurrentContext()
                ?? throw new InvalidOperationException("SideEffect requires a composition.");
            context.StartReplaceableGroup(SideEffectKeyGroup);
            try
            {
                if (context.Changed(key))
                {
                    context.RecordSideEffect(effect);
                }
            }
            finally
            {
                context.EndReplaceableGroup(SideEffectKeyGroup);
            }
        }

        /// <summary>Runs on entry or when the number or value of any key changes. Keys are read during composition.</summary>
        [Composable(ComposableMode.NonRestartable)]
        public static void SideEffect(ReadOnlySpan<object?> keys, Action effect)
        {
            if (effect == null) { throw new ArgumentNullException(nameof(effect)); }
            IComposerContext context = CurrentContext()
                ?? throw new InvalidOperationException("SideEffect requires a composition.");
            context.StartReplaceableGroup(SideEffectKeysGroup);
            try
            {
                bool invalid = context.Changed(keys.Length);
                foreach (object? key in keys)
                {
                    invalid |= context.Changed(key);
                }
                if (invalid)
                {
                    context.RecordSideEffect(effect);
                }
            }
            finally
            {
                context.EndReplaceableGroup(SideEffectKeysGroup);
            }
        }
    }
}
