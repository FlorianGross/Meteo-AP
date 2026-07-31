using ElwMeteo.Core.Models;
using ElwMeteo.Core.Services;
using Xunit;

namespace ElwMeteo.Core.Tests;

public class WarningMonitorTests
{
    private static readonly DateTimeOffset Start =
        new(2026, 3, 14, 12, 0, 0, TimeSpan.Zero);

    private static DwdWarning Warning(
        string name = "STURMBÖEN",
        WarningLevel level = WarningLevel.Moderate,
        string region = "Stadt Frankfurt am Main",
        DateTimeOffset? start = null) =>
        new(name, $"Amtliche Warnung vor {name}", level, start ?? Start, Start.AddHours(6), region, null, null);

    /// <summary>
    /// Somebody who has just started the application is looking at the screen.
    /// The alert exists for the warning that arrives an hour later.
    /// </summary>
    [Fact]
    public void TheFirstObservationAlertsAboutNothing()
    {
        var monitor = new WarningMonitor();

        Assert.Empty(monitor.Observe([Warning(level: WarningLevel.Severe)]));
    }

    [Fact]
    public void AWarningThatArrivesLaterIsAnAlert()
    {
        var monitor = new WarningMonitor();
        monitor.Observe([]);

        IReadOnlyList<WarningAlert> alerts = monitor.Observe([Warning()]);

        Assert.Single(alerts);
        Assert.Equal(AlertReason.New, alerts[0].Reason);
    }

    /// <summary>
    /// Warnings are re-fetched every few minutes. Alerting each time is how
    /// people learn to ignore the alert.
    /// </summary>
    [Fact]
    public void TheSameWarningDoesNotAlertAgainOnTheNextFetch()
    {
        var monitor = new WarningMonitor();
        monitor.Observe([]);
        monitor.Observe([Warning()]);

        Assert.Empty(monitor.Observe([Warning()]));
        Assert.Empty(monitor.Observe([Warning()]));
    }

    [Fact]
    public void AWarningThatGoesUpALevelAlertsAgain()
    {
        var monitor = new WarningMonitor();
        monitor.Observe([]);
        monitor.Observe([Warning(level: WarningLevel.Moderate)]);

        IReadOnlyList<WarningAlert> alerts = monitor.Observe([Warning(level: WarningLevel.Severe)]);

        Assert.Single(alerts);
        Assert.Equal(AlertReason.Escalated, alerts[0].Reason);
        Assert.Contains("verschärft", alerts[0].Describe(), StringComparison.Ordinal);
    }

    [Fact]
    public void AWarningThatIsDowngradedDoesNotAlert()
    {
        var monitor = new WarningMonitor();
        monitor.Observe([]);
        monitor.Observe([Warning(level: WarningLevel.Severe)]);

        Assert.Empty(monitor.Observe([Warning(level: WarningLevel.Moderate)]));
    }

    /// <summary>
    /// Providers reword a headline when they update a warning while the warning
    /// itself is unchanged. Keying on the headline would alert every time.
    /// </summary>
    [Fact]
    public void ARewordedHeadlineIsStillTheSameWarning()
    {
        var monitor = new WarningMonitor();
        monitor.Observe([]);
        monitor.Observe([Warning()]);

        var reworded = Warning() with { Headline = "Aktualisierte Warnung vor STURMBÖEN" };

        Assert.Empty(monitor.Observe([reworded]));
    }

    [Fact]
    public void TheSameEventInAnotherRegionIsANewWarning()
    {
        var monitor = new WarningMonitor();
        monitor.Observe([]);
        monitor.Observe([Warning(region: "Stadt Frankfurt am Main")]);

        IReadOnlyList<WarningAlert> alerts = monitor.Observe(
        [
            Warning(region: "Stadt Frankfurt am Main"),
            Warning(region: "Hochtaunuskreis")
        ]);

        Assert.Single(alerts);
        Assert.Equal("Hochtaunuskreis", alerts[0].Warning.RegionName);
    }

    [Fact]
    public void WarningsBelowTheThresholdStayQuiet()
    {
        var monitor = new WarningMonitor { Threshold = WarningLevel.Severe };
        monitor.Observe([]);

        Assert.Empty(monitor.Observe([Warning(level: WarningLevel.Moderate)]));
        Assert.Single(monitor.Observe([Warning(level: WarningLevel.Moderate), Warning("ORKAN", WarningLevel.Severe)]));
    }

    [Fact]
    public void ThresholdMinorAlertsOnEverything()
    {
        var monitor = new WarningMonitor { Threshold = WarningLevel.Minor };
        monitor.Observe([]);

        Assert.Single(monitor.Observe([Warning(level: WarningLevel.Minor)]));
    }

    /// <summary>
    /// Expired warnings are forgotten, so the same event next week alerts again
    /// and the dictionary does not grow for the life of the process.
    /// </summary>
    [Fact]
    public void AWarningThatExpiredAndReturnsAlertsAgain()
    {
        var monitor = new WarningMonitor();
        monitor.Observe([]);
        monitor.Observe([Warning()]);
        monitor.Observe([]);

        Assert.Equal(0, monitor.TrackedCount);
        Assert.Single(monitor.Observe([Warning()]));
    }

    [Fact]
    public void SeveralAtOnceAreOrderedWorstFirst()
    {
        var monitor = new WarningMonitor();
        monitor.Observe([]);

        IReadOnlyList<WarningAlert> alerts = monitor.Observe(
        [
            Warning("GEWITTER", WarningLevel.Moderate),
            Warning("ORKANBÖEN", WarningLevel.Extreme),
            Warning("STARKREGEN", WarningLevel.Severe)
        ]);

        Assert.Equal(3, alerts.Count);
        Assert.Equal(WarningLevel.Extreme, alerts[0].Warning.Level);
        Assert.Equal(WarningLevel.Severe, alerts[1].Warning.Level);
    }

    [Fact]
    public void ResetMakesTheNextObservationPrimeAgain()
    {
        var monitor = new WarningMonitor();
        monitor.Observe([]);
        monitor.Observe([Warning()]);
        monitor.Reset();

        Assert.Empty(monitor.Observe([Warning()]));
        Assert.Equal(0, new WarningMonitor().TrackedCount);
    }

    [Fact]
    public void TheAlertTextNamesTheEventAndTheLevel()
    {
        var monitor = new WarningMonitor();
        monitor.Observe([]);

        string text = monitor.Observe([Warning("ORKANBÖEN", WarningLevel.Extreme)])[0].Describe();

        Assert.Contains("ORKANBÖEN", text, StringComparison.Ordinal);
        Assert.Contains("Stufe 4", text, StringComparison.Ordinal);
    }
}
