using DotNetCompose.Runtime.Snapshots;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace DotNetCompose.Maui.Drawing;

internal sealed class ObservedDrawable : IDrawable, IDisposable
{
    private readonly GraphicsView _view;
    private readonly ObserverHandle _observer;
    private readonly object _gate = new();
    private HashSet<object> _reads = new(ReferenceEqualityComparer.Instance);
    private Action<ICanvas, RectF> _draw;
    private bool _disposed;

    internal int InvalidationCount { get; private set; }

    internal ObservedDrawable(GraphicsView view, Action<ICanvas, RectF> draw)
    {
        _view = view;
        _draw = draw;
        _observer = Snapshot.RegisterApplyObserver((states, _) =>
        {
            lock (_gate)
            {
                if (_disposed || !states.Any(state => _reads.Contains(state)))
                {
                    return;
                }
            }
            Invalidate();
        });
        view.Drawable = this;
    }

    internal void Update(Action<ICanvas, RectF> draw)
    {
        _draw = draw;
        Invalidate();
    }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        HashSet<object> reads = new(ReferenceEqualityComparer.Instance);
        Snapshot.Observe(state => reads.Add(state), null, () => _draw(canvas, dirtyRect));
        lock (_gate)
        {
            if (!_disposed)
            {
                _reads = reads;
            }
        }
    }

    private void Invalidate()
    {
        InvalidationCount++;
        if (_view.Handler is null)
        {
            if (!_disposed)
            {
                _view.Invalidate();
            }
            return;
        }
        if (_view.Dispatcher.IsDispatchRequired)
        {
            _view.Dispatcher.Dispatch(() =>
            {
                if (!_disposed)
                {
                    _view.Invalidate();
                }
            });
        }
        else if (!_disposed)
        {
            _view.Invalidate();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            _reads.Clear();
        }
        _observer.Dispose();
        if (ReferenceEquals(_view.Drawable, this))
        {
            _view.Drawable = null!;
        }
    }
}
