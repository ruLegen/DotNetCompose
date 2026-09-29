using System;

namespace DotNetCompose.Runtime.Composer
{
    public interface IComposition : IDisposable
    {
        bool IsDisposed { get; }
        void SetContent(ComposableAction content);
    }
}
