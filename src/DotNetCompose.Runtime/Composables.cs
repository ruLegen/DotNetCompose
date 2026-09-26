using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DotNetCompose.Runtime.Composer;
using DotNetCompose.Runtime.Effects;
using DotNetCompose.Runtime.Snapshots;

namespace DotNetCompose.Runtime
{
    public static partial class Composables
    {
        internal static readonly object Empty = new object();

        public partial class Builders
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static IComposerContext CurrentContext(
                IComposerContext context,
                ComposableArgumentsState changed = default,
                ComposableArgumentsDefaultState defaultState = default)
            {
                return context;
            }
        }

        [Composable(ComposableMode.ReadOnly), ComposableIgnore]
        public static IComposerContext? CurrentContext()
        {
            throw new NotImplementedException("Internal usage only");
        }

        [Composable(ComposableMode.ExplicitGroups)]
        public static void ComposeNode<T, K>(Func<T> factory, Action<T> updater) where T : class
        {
            ComposeNode(factory, updater);
        }

        [Composable(ComposableMode.ExplicitGroups)]
        public static void ComposeNode<T>(Func<T> factory, Action<T> updater) where T : class
        {
            IComposerContext context = CurrentContext()
                ?? throw new InvalidOperationException("ComposeNode can only be used during composition.");

            context.StartNode();
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
        public static void ComposeNode<T>(
            Func<T> factory,
            Action<T> updater,
            [Composable(ComposableMode.Inline)] Action content) where T : class
        {
            IComposerContext context = CurrentContext()
                ?? throw new InvalidOperationException("ComposeNode can only be used during composition.");

            context.StartNode();
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

        [Composable(ComposableMode.ExplicitGroups)]
        public static void Key(object? key, [Composable(ComposableMode.Inline)] Action content)
        {
            IComposerContext context = CurrentContext()
                ?? throw new InvalidOperationException("Key can only be used during composition.");

            context.StartMovableGroup(0, key);
            try
            {
                content();
            }
            finally
            {
                context.EndMovableGroup(0);
            }
        }

        [Composable(ComposableMode.NonSkippable)]
        public static void CompositionLocalProvider(ProvidedValue value, [Composable] Action content)
        {
            if (content == null)
            {
                throw new ArgumentNullException(nameof(content));
            }
            IComposerContext context = CurrentContext()
                ?? throw new InvalidOperationException("CompositionLocalProvider can only be used during composition.");

            context.StartProvider(value);
            try
            {
                content();
            }
            finally
            {
                context.EndProvider();
            }
        }

        [Composable(ComposableMode.NonSkippable)]
        public static void CompositionLocalProvider(IReadOnlyList<ProvidedValue> values, [Composable] Action content)
        {
            if (values == null)
            {
                throw new ArgumentNullException(nameof(values));
            }
            if (content == null)
            {
                throw new ArgumentNullException(nameof(content));
            }
            IComposerContext context = CurrentContext()
                ?? throw new InvalidOperationException("CompositionLocalProvider can only be used during composition.");

            context.StartProviders(values);
            try
            {
                content();
            }
            finally
            {
                context.EndProviders();
            }
        }

        [Composable(ComposableMode.Inline)]
        public static T Remember<T>(object key, Func<T> creator)
        {
            IComposerContext context = CurrentContext()
                ?? throw new InvalidOperationException("Remember can only be used during composition.");

            bool invalid = context.Changed(key);
            object? slot = context.RememberedValue();
            if (ReferenceEquals(slot, Empty) || invalid)
            {
                T value = creator();
                context.UpdateRememberedValue(value);
                return value;
            }

            return (T)slot!;
        }

        [Composable(ComposableMode.NonRestartable)]
        public static void LaunchedEffect(object? key1, Func<CancellationToken, ValueTask> block)
        {
            IComposerContext context = CurrentContext()
                ?? throw new InvalidOperationException("LaunchedEffect can only be used during composition.");

            bool invalid = context.Changed(key1);
            object? slot = context.RememberedValue();
            if (ReferenceEquals(slot, Empty) || invalid)
            {
                LaunchedEffectJob job = new LaunchedEffectJob(block, context.ReportEffectError);
                context.UpdateRememberedValue(job);
            }
        }

        public static SnapshotMutableState<T> CreateMutableState<T>(T value, ISnapshotMutationPolicy<T>? policy = null)
        {
            return Snapshots.SnapshotMutableStateFactory.Create(value, policy);
        }

        public static ProvidableCompositionLocal<T> CompositionLocalOf<T>(
            Func<T> defaultFactory,
            ISnapshotMutationPolicy<T>? policy = null)
        {
            if (defaultFactory == null)
            {
                throw new ArgumentNullException(nameof(defaultFactory));
            }
            return new DynamicProvidableCompositionLocal<T>(defaultFactory, policy ?? StructuralPolicy<T>.Default);
        }

        public static ProvidableCompositionLocal<T> StaticCompositionLocalOf<T>(Func<T> defaultFactory)
        {
            if (defaultFactory == null)
            {
                throw new ArgumentNullException(nameof(defaultFactory));
            }
            return new StaticProvidableCompositionLocal<T>(defaultFactory);
        }
    }
}
