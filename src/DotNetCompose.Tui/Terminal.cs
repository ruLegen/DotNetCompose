using System.Collections.Concurrent;
using System.Text;

namespace DotNetCompose.Tui;

public readonly record struct TuiKeyEvent(ConsoleKey Key, char Character, ConsoleModifiers Modifiers)
{
    public bool Shift => (Modifiers & ConsoleModifiers.Shift) != 0;
    public bool Control => (Modifiers & ConsoleModifiers.Control) != 0;
    public bool Alt => (Modifiers & ConsoleModifiers.Alt) != 0;
}

public interface ITerminalDriver : IDisposable
{
    TuiSize Size { get; }
    bool IsInteractive { get; }
    void Enter();
    void Exit();
    bool TryReadKey(out TuiKeyEvent key);
    void Write(string value);
}

public sealed class AnsiTerminalDriver : ITerminalDriver
{
    private bool _entered;

    public TuiSize Size
    {
        get
        {
            try { return new TuiSize(Math.Max(1, Console.WindowWidth), Math.Max(1, Console.WindowHeight)); }
            catch { return new TuiSize(80, 25); }
        }
    }

    public bool IsInteractive => !Console.IsInputRedirected && !Console.IsOutputRedirected;

    public void Enter()
    {
        if (!IsInteractive)
            throw new InvalidOperationException("TUI requires an interactive terminal. Use FakeTerminalDriver for redirected or automated runs.");
        if (_entered) return;
        Console.OutputEncoding = Encoding.UTF8;
        Console.TreatControlCAsInput = true;
        Console.Write("\u001b[?1049h\u001b[?25l\u001b[0m");
        _entered = true;
    }

    public void Exit()
    {
        if (!_entered) return;
        Console.Write("\u001b[0m\u001b[?25h\u001b[?1049l");
        _entered = false;
    }

    public bool TryReadKey(out TuiKeyEvent key)
    {
        if (!Console.KeyAvailable) { key = default; return false; }
        ConsoleKeyInfo info = Console.ReadKey(intercept: true);
        key = new TuiKeyEvent(info.Key, info.KeyChar, info.Modifiers);
        return true;
    }

    public void Write(string value) => Console.Write(value);
    public void Dispose() => Exit();
}

public sealed class FakeTerminalDriver : ITerminalDriver
{
    private readonly ConcurrentQueue<TuiKeyEvent> _keys = new();
    private readonly StringBuilder _output = new();

    public FakeTerminalDriver(int width = 80, int height = 25) => Size = new TuiSize(width, height);
    public TuiSize Size { get; set; }
    public bool IsInteractive => true;
    public bool Entered { get; private set; }
    public bool Exited { get; private set; }
    public string Output => _output.ToString();

    public void Enqueue(TuiKeyEvent key) => _keys.Enqueue(key);
    public void Enter() => Entered = true;
    public void Exit() { Exited = true; Entered = false; }
    public bool TryReadKey(out TuiKeyEvent key) => _keys.TryDequeue(out key);
    public void Write(string value) => _output.Append(value);
    public void ClearOutput() => _output.Clear();
    public void Dispose() => Exit();
}

internal sealed class TerminalSynchronizationContext : SynchronizationContext
{
    private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _queue = new();
    private readonly AutoResetEvent _signal = new(false);
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;

    public override void Post(SendOrPostCallback d, object? state)
    {
        ArgumentNullException.ThrowIfNull(d);
        _queue.Enqueue((d, state));
        _signal.Set();
    }

    public override void Send(SendOrPostCallback d, object? state)
    {
        ArgumentNullException.ThrowIfNull(d);
        if (Environment.CurrentManagedThreadId == _ownerThread) { d(state); return; }
        using ManualResetEventSlim completed = new();
        Exception? error = null;
        Post(value =>
        {
            try { d(value); }
            catch (Exception exception) { error = exception; }
            finally { completed.Set(); }
        }, state);
        completed.Wait();
        if (error != null) throw new AggregateException(error);
    }

    internal bool Drain()
    {
        bool handled = false;
        while (_queue.TryDequeue(out var work))
        {
            handled = true;
            work.Callback(work.State);
        }
        return handled;
    }

    internal void Wait(int milliseconds) => _signal.WaitOne(milliseconds);
}
