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
    public static class Composables
    {
        internal static readonly object Empty = new object();

        public partial class Builders
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static IComposerContext CurrentContext(IComposerContext context, ComposableArgumentsState changed = default, ComposableArgumentsDefaultState defaultState = default) => context;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static T Remember<T>(object key, Func<T> creator, IComposerContext context, ComposableArgumentsState changed = default, ComposableArgumentsDefaultState defaultState = default)
            {
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

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static void ComposeNode<T, K>(Func<T> factory, Action<T> updater, IComposerContext context, ComposableArgumentsState changed = default, ComposableArgumentsDefaultState defaultState = default) where T : class
            {
                ComposeNode(factory, updater, context, changed, defaultState);
            }

            public static void ComposeNode<T>(Func<T> factory, Action<T> updater, IComposerContext context,
                ComposableArgumentsState changed = default, ComposableArgumentsDefaultState defaultState = default) where T : class
                => ComposeNode(factory, updater, null, context, changed, defaultState);

            public static void ComposeNode<T>(Func<T> factory, Action<T> updater, ComposableAction? content,
                IComposerContext context, ComposableArgumentsState changed = default, ComposableArgumentsDefaultState defaultState = default) where T : class
            {
                context.StartNode();
                if (context.Inserting) context.CreateNode(factory); else context.UseNode();
                try
                {
                    context.ApplyNode(updater, null);
                    content?.Invoke(context, default, default);
                }
                finally { context.EndNode(); }
            }

            public static void Key(object? key, ComposableAction content, IComposerContext context,
                ComposableArgumentsState changed = default, ComposableArgumentsDefaultState defaultState = default)
            {
                context.StartMovableGroup(0, key);
                try { content(context, default, default); }
                finally { context.EndMovableGroup(0); }
            }

            public static void CompositionLocalProvider(ProvidedValue value, ComposableAction content,
                IComposerContext context, ComposableArgumentsState changed = default,
                ComposableArgumentsDefaultState defaultState = default)
            {
                if (content == null) throw new ArgumentNullException(nameof(content));
                context.StartProvider(value);
                try { content(context, default, default); }
                finally { context.EndProvider(); }
            }

            public static void CompositionLocalProvider(IReadOnlyList<ProvidedValue> values, ComposableAction content,
                IComposerContext context, ComposableArgumentsState changed = default,
                ComposableArgumentsDefaultState defaultState = default)
            {
                if (content == null) throw new ArgumentNullException(nameof(content));
                context.StartProviders(values);
                try { content(context, default, default); }
                finally { context.EndProviders(); }
            }

           
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static void LaunchedEffect(object? key1, Func<CancellationToken, ValueTask> block,
                IComposerContext context, ComposableArgumentsState changed = default, ComposableArgumentsDefaultState defaultState = default)
            {
                bool invalid = context.Changed(key1);
                object? slot = context.RememberedValue();
                if (ReferenceEquals(slot, Empty) || invalid)
                {
                    LaunchedEffectJob job = new LaunchedEffectJob(block);
                    context.UpdateRememberedValue(job);
                }
            }
        }

        [Composable, ComposableIgnore]
        public static IComposerContext? CurrentContext() => throw new NotImplementedException("Internal usage only");

        [Composable, ComposableIgnore]
        public static void ComposeNode<T, K>(Func<T> factory, Action<T> updater) where T : class => throw new NotImplementedException("Use composable version");

        [Composable, ComposableIgnore]
        public static void ComposeNode<T>(Func<T> factory, Action<T> updater) where T : class => throw new NotImplementedException("Use composable version");

        [Composable, ComposableIgnore]
        public static void ComposeNode<T>(Func<T> factory, Action<T> updater, [Composable] Action content) where T : class => throw new NotImplementedException("Use composable version");

        [Composable, ComposableIgnore]
        public static void Key(object? key, [Composable] Action content) => throw new NotImplementedException("Use composable version");

        [Composable, ComposableIgnore]
        public static void CompositionLocalProvider(ProvidedValue value, [Composable] Action content)
        {
            IComposerContext context = ComposeScope.GetCurrentContext()
                ?? throw new InvalidOperationException("CompositionLocalProvider can only be used during composition.");
            Builders.CompositionLocalProvider(value, (current, _, _) => content(), context);
        }

        [Composable, ComposableIgnore]
        public static void CompositionLocalProvider(IReadOnlyList<ProvidedValue> values, [Composable] Action content)
        {
            IComposerContext context = ComposeScope.GetCurrentContext()
                ?? throw new InvalidOperationException("CompositionLocalProvider can only be used during composition.");
            Builders.CompositionLocalProvider(values, (current, _, _) => content(), context);
        }

        [Composable, ComposableIgnore]
        public static T Remember<T>(object key, Func<T> creator) => throw new NotImplementedException("Use composable version");

        [Composable, ComposableIgnore]
        public static void LaunchedEffect(object? key1, Func<CancellationToken, ValueTask> block)
            => throw new NotImplementedException("Use composable version");

        public static SnapshotMutableState<T> CreateMutableState<T>(T value, ISnapshotMutationPolicy<T>? policy = null)
        {
            return Snapshots.SnapshotMutableStateFactory.Create(value, policy);
        }

        public static ProvidableCompositionLocal<T> CompositionLocalOf<T>(
            Func<T> defaultFactory,
            ISnapshotMutationPolicy<T>? policy = null)
        {
            if (defaultFactory == null) throw new ArgumentNullException(nameof(defaultFactory));
            return new DynamicProvidableCompositionLocal<T>(defaultFactory, policy ?? StructuralPolicy<T>.Default);
        }

        public static ProvidableCompositionLocal<T> StaticCompositionLocalOf<T>(Func<T> defaultFactory)
        {
            if (defaultFactory == null) throw new ArgumentNullException(nameof(defaultFactory));
            return new StaticProvidableCompositionLocal<T>(defaultFactory);
        }
    }
}
