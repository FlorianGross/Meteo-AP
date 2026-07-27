using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using ElwMeteo.Desktop.Views;
using Xunit;

[assembly: Avalonia.Headless.AvaloniaTestApplication(typeof(ElwMeteo.Desktop.Tests.TestAppBuilder))]

namespace ElwMeteo.Desktop.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<ElwMeteo.Desktop.App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

/// <summary>
/// Every view is built once, for real, on a headless platform.
///
/// The XAML compiler catches a misspelt element but not a missing resource or a
/// broken template — those only surface when the control is actually
/// constructed, which on a Windows-only head could never be checked here at all.
/// This is the thing the cross-platform port buys: the interface is testable on
/// the build machine.
/// </summary>
public class ViewSmokeTests
{
    [AvaloniaTheory]
    [InlineData(typeof(ClockView))]
    [InlineData(typeof(DashboardView))]
    [InlineData(typeof(MapView))]
    [InlineData(typeof(WebRadarView))]
    [InlineData(typeof(TrendView))]
    [InlineData(typeof(DiagnosticsView))]
    [InlineData(typeof(SettingsView))]
    public void EveryViewLoadsItsXaml(Type viewType)
    {
        var view = (UserControl)Activator.CreateInstance(viewType)!;

        Assert.NotNull(view.Content);
    }

    [AvaloniaFact]
    public void TheShellWindowLoads()
    {
        var window = new MainWindow();

        Assert.NotNull(window.Content);
        Assert.Equal("ELW-Meteo", window.Title);
    }
}
