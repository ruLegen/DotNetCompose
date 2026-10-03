namespace DotNetCompose.Maui;

public sealed class HotReloadErrorEventArgs : EventArgs
{
    public HotReloadErrorEventArgs(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        Exception = exception;
    }

    public Exception Exception
    {
        get;
    }
}
