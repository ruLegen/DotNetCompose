
namespace DotNetCompose.Runtime.Composer
{
    public interface IComposerContext : System.IDisposable
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
        void EndMovableGroup(int key);

        bool Changed<T>(T value);

        object? RememberedValue();
        void UpdateRememberedValue(object? value);

        void CreateNode<T>(System.Func<T> factory) where T : class;
        void ApplyNode<T>(System.Action<T> block, object? value);

        void ComposeContent(ComposableAction content);

        bool Skipping { get; }
        void SkipToGroupEnd();

        bool Inserting { get; }
        bool IsComposing { get; }
    }
}
