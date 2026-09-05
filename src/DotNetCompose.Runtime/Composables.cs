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
                var invalid = context.Changed(key);
                var slot = context.RememberedValue();
                if (ReferenceEquals(slot, Empty) || invalid)
                {
                    var value = creator();
                    context.UpdateRememberedValue(value);
                    return value;
                }
                return (T)slot!;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static void ComposeNode<T, K>(Func<T> factory, Action<T> updater, IComposerContext context, ComposableArgumentsState changed = default, ComposableArgumentsDefaultState defaultState = default)
            {
            }

           
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static void LaunchedEffect(object? key1, Func<CancellationToken, ValueTask> block,
                IComposerContext context, ComposableArgumentsState changed = default, ComposableArgumentsDefaultState defaultState = default)
            {
                var invalid = context.Changed(key1);
                var slot = context.RememberedValue();
                if (ReferenceEquals(slot, Empty) || invalid)
                {
                    var job = new LaunchedEffectJob(block);
                    context.UpdateRememberedValue(job);
                }
            }
        }

        [Composable, ComposableIgnore]
        public static IComposerContext? CurrentContext() => throw new NotImplementedException("Internal usage only");

        [Composable, ComposableIgnore]
        public static void ComposeNode<T, K>(Func<T> factory, Action<T> updater) => throw new NotImplementedException("Use composable version");

        [Composable, ComposableIgnore]
        public static T Remember<T>(object key, Func<T> creator) => throw new NotImplementedException("Use composable version");

        [Composable, ComposableIgnore]
        public static void LaunchedEffect(object? key1, Func<CancellationToken, ValueTask> block)
            => throw new NotImplementedException("Use composable version");

        public static SnapshotMutableState<T> CreateMutableState<T>(T value, ISnapshotMutationPolicy<T>? policy = null)
        {
            return Snapshots.SnapshotMutableStateFactory.Create(value, policy);
        }
    }
}
