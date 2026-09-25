
using System;
using System.Collections.Generic;

namespace DotNetCompose.Runtime.Composer
{
    public interface IComposerContext : IDisposable
    {
        void StartRoot();
        void EndRoot();

        void StartGroup(int key);
        void EndGroup();

        void StartRestartableGroup(int key);
        IComposeUpdateScope? EndRestartableGroup(int key);
        void StartReplaceableGroup(int key);
        void EndReplaceableGroup(int key);
        void StartMovableGroup(int key);
        void StartMovableGroup(int key, object? dataKey) => StartMovableGroup(key);
        void EndMovableGroup(int key);

        bool Changed<T>(T value);

        object? RememberedValue();
        void UpdateRememberedValue(object? value);

        void CreateNode<T>(System.Func<T> factory) where T : class;
        void StartNode(int key = 0) => throw new NotSupportedException();
        void UseNode() => throw new NotSupportedException();
        void EndNode() => throw new NotSupportedException();
        void ApplyNode<T, TValue>(TValue value, Action<T, TValue> block) => ApplyNode<T>(node => block(node, value), value);
        void ApplyNode<T>(System.Action<T> block, object? value);

        void ComposeContent(ComposableAction content);

        void StartProvider(ProvidedValue value) => throw new NotSupportedException();
        void EndProvider() => throw new NotSupportedException();
        void StartProviders(IReadOnlyList<ProvidedValue> values) => throw new NotSupportedException();
        void EndProviders() => throw new NotSupportedException();
        T Consume<T>(CompositionLocal<T> key) => throw new NotSupportedException();

        bool Skipping { get; }
        void SkipToGroupEnd();

        bool Inserting { get; }
        bool IsComposing { get; }
        void ReportEffectError(Exception error) => throw error;
    }
}
