using System.Runtime.ExceptionServices;
using DotNetCompose.Runtime;
using DotNetCompose.Runtime.Composer;

namespace DotNetCompose.Tui;

public static class TuiApplication
{
    public static void Run(ComposableAction root, ITerminalDriver? terminal = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(root);
        bool ownsTerminal = terminal == null;
        terminal ??= new AnsiTerminalDriver();
        if (!terminal.IsInteractive)
            throw new InvalidOperationException("TUI requires an interactive terminal. Use FakeTerminalDriver for tests.");

        SynchronizationContext? previousContext = SynchronizationContext.Current;
        TerminalSynchronizationContext context = new();
        SynchronizationContext.SetSynchronizationContext(context);
        bool renderRequested = true;
        Exception? pendingError = null;

        try
        {
            terminal.Enter();
            TuiApplier applier = new(() => renderRequested = true);
            using Recomposer recomposer = new(context);
            using Composition<TuiNode> composition = new(applier, recomposer);
            recomposer.Error += (_, args) => pendingError = args.Exception;
            FocusManager focus = new();
            TuiRenderer renderer = new(terminal);
            TuiSize lastSize = terminal.Size;
            composition.SetContent(root);

            while (!cancellationToken.IsCancellationRequested)
            {
                if (context.Drain()) renderRequested = true;
                if (pendingError != null) ExceptionDispatchInfo.Capture(pendingError).Throw();

                TuiSize size = terminal.Size;
                if (size != lastSize)
                {
                    lastSize = size;
                    renderer.Reset();
                    renderRequested = true;
                }

                if (renderRequested)
                {
                    renderer.Render(applier.Root, size, focus, TuiTheme.Default);
                    renderRequested = false;
                }

                if (terminal.TryReadKey(out TuiKeyEvent key))
                {
                    if (key.Key == ConsoleKey.Escape ||
                        (key.Control && key.Key is ConsoleKey.C or ConsoleKey.Q))
                        break;
                    if (focus.HandleKey(key)) renderRequested = true;
                    continue;
                }

                context.Wait(16);
            }
        }
        finally
        {
            try { terminal.Exit(); }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previousContext);
                if (ownsTerminal) terminal.Dispose();
            }
        }
    }
}
