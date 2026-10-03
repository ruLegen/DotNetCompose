using System;
using DotNetCompose.Runtime.Composer;

namespace DotNetCompose.Runtime
{
    public static partial class Composables
    {
        private const int ReusableContentGroup = -19101;
        private const int ReusableHostGroup = -19102;

        /// <summary>Recreates remembered state when the key changes, retaining structurally matching reusable nodes.</summary>
        [Composable(ComposableMode.ExplicitGroups)]
        public static void ReusableContent(object? key, [Composable(ComposableMode.Inline)] Action content)
        {
            IComposerContext context = CurrentContext()
                ?? throw new InvalidOperationException("ReusableContent requires a composition.");
            context.StartReusableGroup(ReusableContentGroup, key);
            try
            {
                content();
            }
            finally
            {
                context.EndReusableGroup(ReusableContentGroup);
            }
        }

        /// <summary>Deactivates remembered state while retaining physical nodes when inactive.</summary>
        [Composable(ComposableMode.ExplicitGroups)]
        public static void ReusableContentHost(bool active, [Composable(ComposableMode.Inline)] Action content)
        {
            IComposerContext context = CurrentContext()
                ?? throw new InvalidOperationException("ReusableContentHost requires a composition.");
            context.StartReusableGroup(ReusableHostGroup, active);
            try
            {
                if (active)
                {
                    content();
                }
                else
                {
                    context.DeactivateToEndGroup();
                }
            }
            finally
            {
                context.EndReusableGroup(ReusableHostGroup);
            }
        }

        /// <summary>Emits a node that can survive a change of reusable content identity.</summary>
        [Composable(ComposableMode.ExplicitGroups)]
        public static void ReusableComposeNode<T>(Func<T> factory, Action<T> updater) where T : class
        {
            IComposerContext context = CurrentContext()
                ?? throw new InvalidOperationException("ReusableComposeNode requires a composition.");
            context.StartReusableNode();
            if (context.Inserting)
            {
                context.CreateNode(factory);
            }
            else
            {
                context.UseNode();
            }
            try
            {
                context.ApplyNode(updater, null);
            }
            finally
            {
                context.EndNode();
            }
        }

        [Composable(ComposableMode.ExplicitGroups)]
        public static void ReusableComposeNode<T>(
            Func<T> factory, Action<T> updater,
            [Composable(ComposableMode.Inline)] Action content) where T : class
        {
            IComposerContext context = CurrentContext()
                ?? throw new InvalidOperationException("ReusableComposeNode requires a composition.");
            context.StartReusableNode();
            if (context.Inserting)
            {
                context.CreateNode(factory);
            }
            else
            {
                context.UseNode();
            }
            try
            {
                context.ApplyNode(updater, null);
                content();
            }
            finally
            {
                context.EndNode();
            }
        }
    }
}
