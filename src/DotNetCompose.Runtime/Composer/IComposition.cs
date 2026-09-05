namespace DotNetCompose.Runtime.Composer
{
    public interface IComposition
    {
        bool IsDisposed { get; }
        void Dispose();
        void SetContent(ComposableAction content);
    }
}
