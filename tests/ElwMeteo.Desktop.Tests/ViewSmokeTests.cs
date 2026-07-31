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

    /// <summary>
    /// The alert banner sits in its own grid row above the tabs. Adding a row
    /// means every row index below it shifts, and a control left on the old
    /// index lands silently on top of another one rather than failing to build —
    /// so the row assignments are asserted rather than eyeballed.
    /// </summary>
    [AvaloniaFact]
    public void TheShellRowsAreNotOffByOne()
    {
        var window = new MainWindow();
        var grid = (Grid)window.Content!;

        Assert.Equal(5, grid.RowDefinitions.Count);

        var tabs = grid.Children.OfType<TabControl>().Single();
        Assert.Equal(3, Grid.GetRow(tabs));

        // Header, alert, warning summary and status bar: one per remaining row,
        // none sharing.
        int[] rows = grid.Children.Where(c => c is not TabControl).Select(Grid.GetRow).Order().ToArray();
        Assert.Equal([0, 1, 2, 4], rows);
    }

    [AvaloniaFact]
    public void EveryTabHasItsView()
    {
        var window = new MainWindow();
        var tabs = ((Grid)window.Content!).Children.OfType<TabControl>().Single();

        Assert.Equal(7, tabs.Items.Count);
        Assert.All(tabs.Items, item => Assert.NotNull(((TabItem)item!).Content));
    }
}
