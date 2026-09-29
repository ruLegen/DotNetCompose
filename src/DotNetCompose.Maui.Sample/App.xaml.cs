namespace DotNetCompose.Maui.Sample;

public partial class App : Application
{
    private readonly IServiceProvider _services;

    public App(IServiceProvider services)
    {
        InitializeComponent();
        _services = services;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        MainPage page = new(_services);
        Window window = new(page);
        window.Destroying += (_, _) => page.Dispose();
        return window;
    }
}
