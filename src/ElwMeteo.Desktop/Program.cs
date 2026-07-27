using System.Globalization;
using Avalonia;

namespace ElwMeteo.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Dates, decimal commas and weekday names must come out German whatever
        // the machine's locale — the same rule as on the Windows head.
        var german = CultureInfo.GetCultureInfo("de-DE");
        CultureInfo.DefaultThreadCurrentCulture = german;
        CultureInfo.DefaultThreadCurrentUICulture = german;

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Referenced by name from the Avalonia designer and the headless test host.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
