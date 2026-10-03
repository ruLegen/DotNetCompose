using DotNetCompose.Runtime;
using Microsoft.Maui.Controls;

namespace DotNetCompose.Maui;

/// <summary>Observes one page and its window. Dispose when the owning page is released.</summary>
public sealed class MauiTaskActivities : IDisposable
{
    private readonly Page _page;
    private readonly Window _window;
    private readonly Activity _pageVisible;
    private readonly Activity _windowVisible;
    private bool _disposed;

    /// <summary>Pass explicit initial visibility when attaching after the first appearance/activation.</summary>
    public MauiTaskActivities(Page page, Window window, bool pageInitiallyVisible = false, bool windowInitiallyVisible = false)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(window);
        _page = page;
        _window = window;
        _pageVisible = new Activity(pageInitiallyVisible);
        _windowVisible = new Activity(windowInitiallyVisible);
        PageAndWindowVisible = TaskActivity.All(_pageVisible, _windowVisible);
        page.Appearing += OnAppearing;
        page.Disappearing += OnDisappearing;
        window.Activated += OnActivated;
        window.Resumed += OnActivated;
        window.Stopped += OnStopped;
        window.Destroying += OnStopped;
    }

    public ITaskActivity PageVisible => _pageVisible;
    public ITaskActivity WindowVisible => _windowVisible;
    public ITaskActivity PageAndWindowVisible { get; }

    private void OnAppearing(object? sender, EventArgs args) => _pageVisible.Set(true);
    private void OnDisappearing(object? sender, EventArgs args) => _pageVisible.Set(false);
    private void OnActivated(object? sender, EventArgs args) => _windowVisible.Set(true);
    private void OnStopped(object? sender, EventArgs args) => _windowVisible.Set(false);

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _page.Appearing -= OnAppearing;
        _page.Disappearing -= OnDisappearing;
        _window.Activated -= OnActivated;
        _window.Resumed -= OnActivated;
        _window.Stopped -= OnStopped;
        _window.Destroying -= OnStopped;
        _pageVisible.Set(false);
        _windowVisible.Set(false);
    }

    private sealed class Activity(bool initial) : ITaskActivity
    {
        public bool IsActive { get; private set; } = initial;
        public event EventHandler? Changed;

        internal void Set(bool value)
        {
            if (IsActive == value)
                return;
            IsActive = value;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
