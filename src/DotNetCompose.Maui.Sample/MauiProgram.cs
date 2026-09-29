using DotNetCompose.Maui.Sample.Services;
using Microsoft.Extensions.DependencyInjection;

namespace DotNetCompose.Maui.Sample;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        MauiAppBuilder builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });
        builder.Services.AddSingleton<SampleGreetingService>();
        return builder.Build();
    }
}
