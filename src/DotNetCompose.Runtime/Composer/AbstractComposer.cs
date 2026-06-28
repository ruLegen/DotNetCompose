using System;
using System.Collections.Generic;
using DotNetCompose.Runtime.CompositionLocal;

namespace DotNetCompose.Runtime.Composer
{
    public abstract class AbstractComposer : IComposerContext
    {
        private readonly Stack<int> _groupKeys = new Stack<int>();
        private int _groupDepth;
        private bool _disposed;
        private bool _isComposing;
        private bool _inserting;

        // CompositionLocal provider stack
        private readonly Stack<CompositionLocalMap> _providerStack = new Stack<CompositionLocalMap>();
        private CompositionLocalMap _currentLocalMap = CompositionLocalMap.Empty;

        internal int GroupDepth => _groupDepth;
        internal int CurrentGroupKey => _groupKeys.Count > 0 ? _groupKeys.Peek() : -1;
        internal bool Disposed => _disposed;

        public virtual void StartRoot()
        {
            _isComposing = true;
            _currentLocalMap = CompositionLocalMap.Empty;
            _providerStack.Clear();
        }

        public virtual void EndRoot()
        {
            _isComposing = false;
        }

        public virtual void StartGroup(int key)
        {
            _groupKeys.Push(key);
            _groupDepth++;
        }

        public virtual void EndGroup()
        {
            _groupKeys.Pop();
            _groupDepth--;
        }

        public virtual void StartRestartableGroup(int key) => StartGroup(key);
        public virtual IComposeUpdateScope? EndRestartableGroup(int key) { EndGroup(); return null; }
        public void StartReplaceableGroup(int key) => StartGroup(key);
        public void EndReplaceableGroup(int key) => EndGroup();
        public virtual void StartMovableGroup(int key) => StartGroup(key);
        public virtual void EndMovableGroup(int key) => EndGroup();

        public virtual bool Changed<T>(T value) => true;

        public virtual object? RememberedValue() => ComposerStatics.Empty;
        public virtual void UpdateRememberedValue(object? value) { }

        public virtual void CreateNode<T>(Func<T> factory) where T : class { }
        public virtual void ApplyNode<T>(Action<T> block, object? value) { }

        public virtual void ComposeContent(ComposableAction content)
        {
            StartRoot();
            try
            {
                content(this, default, default);
            }
            finally
            {
                EndRoot();
            }
        }

        public virtual bool Skipping => false;
        public virtual void SkipToGroupEnd() { }

        public bool Inserting => _inserting;
        public bool IsComposing => _isComposing;

        protected void SetInserting(bool value) => _inserting = value;

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                OnDispose();
            }
        }

        protected virtual void OnDispose() { }

        // --- CompositionLocal support ---

        /// <summary>
        /// Gets the current CompositionLocal map (in-memory).
        /// </summary>
        protected CompositionLocalMap CurrentLocalMap => _currentLocalMap;

        public virtual void StartProviders(ProvidedValue[] values)
        {
            if (values == null || values.Length == 0) return;

            _providerStack.Push(_currentLocalMap);
            _currentLocalMap = CompositionLocalMap.Create(values, _currentLocalMap);
        }

        public virtual void EndProviders()
        {
            if (_providerStack.Count > 0)
                _currentLocalMap = _providerStack.Pop();
        }

        public virtual T Consume<T>(CompositionLocal<T> key)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));

            var holder = _currentLocalMap.GetHolder(key);
            if (holder != null)
                return (T?)holder.ReadValue(_currentLocalMap) ?? default!;

            // Fall back to default
            var defaultVal = key.DefaultValueHolder.ReadValue(CompositionLocalMap.Empty);
            return defaultVal is T t ? t : default!;
        }
    }
}
