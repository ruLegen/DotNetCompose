using System;
using System.Collections.Generic;
using DotNetCompose.Runtime.Snapshots;

namespace DotNetCompose.Runtime.Composer
{
    public interface IComposition
    {
        bool IsDisposed { get; }
        void Dispose();
        void SetContent(ComposableAction content);
    }

    internal class Composition<T> : IComposition, RecomposeScopeOwner
    {
        private readonly GapComposer _composer;
        private readonly IApplier<T> _applier;
        private bool _disposed;

        // Observations: maps state objects to recompose scopes that read them
        private readonly Dictionary<IStateObject, List<WeakReference<RecomposeScopeImpl>>> _observations = new();
        // Current pending invalidations
        private readonly Dictionary<RecomposeScopeImpl, object> _invalidations = new();
        // Snapshot apply observer handle
        private ObserverHandle? _applyObserverHandle;

        public Composition(IApplier<T> applier)
            : this(new GapComposer(), applier)
        {
        }

        internal Composition(GapComposer composer, IApplier<T> applier)
        {
            _composer = composer ?? throw new ArgumentNullException(nameof(composer));
            _applier = applier ?? throw new ArgumentNullException(nameof(applier));
            _composer.Owner = this;
            RegisterSnapshotObservers();
        }

        public bool IsDisposed => _disposed;

        public GapComposer Composer => _composer;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _applyObserverHandle?.Dispose();
            _composer.RememberManager.Clear();
        }

        public void SetContent(ComposableAction content)
        {
            if (_disposed)
                throw new InvalidOperationException("Composition is disposed");

            _composer.ComposeContent(content);

            ApplyChanges();
        }

        public void Recompose()
        {
            if (_invalidations.Count == 0) return;

            _composer.UpdateComposerInvalidations(_invalidations);
            _invalidations.Clear();

            _composer.Recompose();

            ApplyChanges();
        }

        private void ApplyChanges()
        {
            var applierObj = (IApplier<object>)(object)_applier;
            applierObj.OnBeginChanges();

            var changeList = _composer.OperationsChangeList;
            if (changeList != null && !changeList.IsEmpty())
            {
                changeList.Execute(_composer.SlotTable, applierObj, _composer.RememberManager);
            }

            _composer.RememberManager.DispatchLifecycle();
            _composer.RememberManager.DispatchSideEffects();
            applierObj.OnEndChanges();
        }

        // --- RecomposeScopeOwner implementation ---

        public InvalidationResult Invalidate(RecomposeScopeImpl scope, object? instance)
        {
            if (scope.DefaultsInScope)
                scope.DefaultsInvalid = true;

            var anchor = scope.Anchor;
            if (anchor == null || !anchor.Valid)
                return InvalidationResult.IGNORED;

            if (!scope.CanRecompose)
                return InvalidationResult.IGNORED;

            _invalidations[scope] = instance!;

            return InvalidationResult.SCHEDULED;
        }

        public void RecomposeScopeReleased(RecomposeScopeImpl scope)
        {
            _invalidations.Remove(scope);
        }

        public void RecordReadOf(object value)
        {
            if (value is IStateObject state)
            {
                if (_composer.InvalidateStack.Count > 0)
                {
                    var currentScope = _composer.InvalidateStack.Peek();
                    currentScope.Used = true;

                    if (!_observations.ContainsKey(state))
                        _observations[state] = new List<WeakReference<RecomposeScopeImpl>>();

                    var refs = _observations[state];
                    if (!refs.Exists(r => r.TryGetTarget(out var s) && s == currentScope))
                    {
                        refs.Add(new WeakReference<RecomposeScopeImpl>(currentScope));
                    }
                }
            }
        }

        // --- Snapshot integration ---

        private void RegisterSnapshotObservers()
        {
            _applyObserverHandle = Snapshot.RegisterApplyObserver(OnSnapshotApply);
        }

        private void OnSnapshotApply(HashSet<IStateObject> modified, Snapshot snapshot)
        {
            foreach (var state in modified)
            {
                InvalidateScopesForState(state);
            }
        }

        private void InvalidateScopesForState(IStateObject state)
        {
            if (_observations.TryGetValue(state, out var refs))
            {
                foreach (var weakRef in refs)
                {
                    if (weakRef.TryGetTarget(out var scope))
                    {
                        scope.Invalidate();
                    }
                }
            }
        }

        public void RecordWriteOf(IStateObject state)
        {
            InvalidateScopesForState(state);
        }
    }
}
