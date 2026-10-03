using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DotNetCompose.Runtime.Composer;
using DotNetCompose.Runtime.Effects;
using DotNetCompose.Runtime.Snapshots;

namespace DotNetCompose.Runtime
{
    public static partial class Composables
    {
        /// <summary>Runs after apply while activity is active; restarts wait for previous cleanup.</summary>
        [Composable(ComposableMode.NonRestartable)]
        public static void LaunchedEffect<TKey>(TKey key, ITaskActivity activity, Func<CancellationToken, ValueTask> block)
        {
            if (activity == null) { throw new ArgumentNullException(nameof(activity)); }
            if (block == null) { throw new ArgumentNullException(nameof(block)); }
            IComposerContext context = CurrentContext() ?? throw new InvalidOperationException("LaunchedEffect requires a composition.");
            ActivityEffect<TKey> effect = RememberAsyncObject(context, () => new ActivityEffect<TKey>(new AsyncOwner(context.ReportEffectError)));
            ApplyAsyncConfiguration(context, () => effect.Configure(key, activity, (token, _) => block(token)));
        }

        [Composable(ComposableMode.NonRestartable)]
        public static IState<AsyncState<T>> RememberAsync<TKey, T>(TKey key, Func<CancellationToken, Task<T>> factory)
        {
            IComposerContext context = CurrentContext() ?? throw new InvalidOperationException("RememberAsync requires a composition.");
            return RememberAsyncCore(context, key, TaskActivity.Always, factory);
        }

        [Composable(ComposableMode.NonRestartable)]
        public static IState<AsyncState<T>> RememberAsync<TKey, T>(TKey key, ITaskActivity activity, Func<CancellationToken, Task<T>> factory)
        {
            IComposerContext context = CurrentContext() ?? throw new InvalidOperationException("RememberAsync requires a composition.");
            return RememberAsyncCore(context, key, activity, factory);
        }

        private static IState<AsyncState<T>> RememberAsyncCore<TKey, T>(IComposerContext context, TKey key, ITaskActivity activity, Func<CancellationToken, Task<T>> factory)
        {
            if (activity == null) throw new ArgumentNullException(nameof(activity));
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            AsyncData<TKey, T> data = RememberAsyncObject(context, () => new AsyncData<TKey, T>(new AsyncOwner(context.ReportEffectError), false));
            data.Prepare(key, activity);
            ApplyAsyncConfiguration(context, () => data.Configure(key, activity, factory));
            return data.State;
        }

        [Composable(ComposableMode.NonRestartable)]
        public static IState<AsyncState<T>> CollectAsState<TKey, T>(TKey key, Func<CancellationToken, IAsyncEnumerable<T>> factory)
        {
            IComposerContext context = CurrentContext() ?? throw new InvalidOperationException("CollectAsState requires a composition.");
            return CollectAsStateCore(context, key, TaskActivity.Always, factory);
        }

        [Composable(ComposableMode.NonRestartable)]
        public static IState<AsyncState<T>> CollectAsState<TKey, T>(TKey key, ITaskActivity activity, Func<CancellationToken, IAsyncEnumerable<T>> factory)
        {
            IComposerContext context = CurrentContext() ?? throw new InvalidOperationException("CollectAsState requires a composition.");
            return CollectAsStateCore(context, key, activity, factory);
        }

        private static IState<AsyncState<T>> CollectAsStateCore<TKey, T>(IComposerContext context, TKey key, ITaskActivity activity, Func<CancellationToken, IAsyncEnumerable<T>> factory)
        {
            if (activity == null) throw new ArgumentNullException(nameof(activity));
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            AsyncData<TKey, T> data = RememberAsyncObject(context, () => new AsyncData<TKey, T>(new AsyncOwner(context.ReportEffectError), true));
            data.Prepare(key, activity);
            ApplyAsyncConfiguration(context, () => data.Configure(key, activity, factory));
            return data.State;
        }

        [Composable(ComposableMode.NonRestartable)]
        public static AsyncAction RememberAsyncAction(Func<CancellationToken, Task> handler)
        {
            IComposerContext context = CurrentContext() ?? throw new InvalidOperationException("RememberAsyncAction requires a composition.");
            return RememberAsyncActionCore(context, TaskActivity.Always, handler);
        }

        [Composable(ComposableMode.NonRestartable)]
        public static AsyncAction RememberAsyncAction(ITaskActivity activity, Func<CancellationToken, Task> handler)
        {
            IComposerContext context = CurrentContext() ?? throw new InvalidOperationException("RememberAsyncAction requires a composition.");
            return RememberAsyncActionCore(context, activity, handler);
        }

        private static AsyncAction RememberAsyncActionCore(IComposerContext context, ITaskActivity activity, Func<CancellationToken, Task> handler)
        {
            if (activity == null) throw new ArgumentNullException(nameof(activity));
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            AsyncAction action = RememberAsyncObject(context, () => new AsyncAction(new AsyncOwner(context.ReportEffectError)));
            ApplyAsyncConfiguration(context, () => action.Configure(activity, handler));
            return action;
        }

        private static T RememberAsyncObject<T>(IComposerContext context, Func<T> create)
        {
            object? value = context.RememberedValue();
            if (!ReferenceEquals(value, Empty))
                return (T)value!;
            T result = create();
            context.UpdateRememberedValue(result);
            return result;
        }

        private static void ApplyAsyncConfiguration(IComposerContext context, Action apply)
        {
            context.RememberedValue();
            context.UpdateRememberedValue(new AppliedAction(apply));
        }
    }
}
