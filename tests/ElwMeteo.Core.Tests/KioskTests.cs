using ElwMeteo.Core.Configuration;
using ElwMeteo.Core.Kiosk;
using Xunit;

namespace ElwMeteo.Core.Tests;

public class AutostartEntryTests
{
    [Fact]
    public void TheWindowsCommandIsQuoted()
    {
        string command = AutostartEntry.BuildWindowsCommand(
            @"C:\Users\Max Mustermann\AppData\Local\Programs\ELW-Meteo\ELW-Meteo.exe");

        // Without the quotes the Run key starts "C:\Users\Max" with the rest as
        // arguments, and nothing comes up after the reboot.
        Assert.StartsWith("\"", command, StringComparison.Ordinal);
        Assert.EndsWith("\"", command, StringComparison.Ordinal);
        Assert.Contains("Max Mustermann", command, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAlreadyQuotedPathIsNotQuotedTwice()
    {
        string command = AutostartEntry.BuildWindowsCommand("\"C:\\Programme\\ELW-Meteo.exe\"");

        Assert.Equal("\"C:\\Programme\\ELW-Meteo.exe\"", command);
    }

    [Fact]
    public void TheDesktopEntryHasTheRequiredKeys()
    {
        string entry = AutostartEntry.BuildDesktopEntry("/opt/elw meteo/ELW-Meteo");

        Assert.StartsWith("[Desktop Entry]", entry, StringComparison.Ordinal);
        Assert.Contains("Type=Application", entry, StringComparison.Ordinal);
        Assert.Contains("Exec=\"/opt/elw meteo/ELW-Meteo\"", entry, StringComparison.Ordinal);
        Assert.Contains("Terminal=false", entry, StringComparison.Ordinal);
    }

    [Fact]
    public void TheDesktopEntryUsesUnixLineEndings()
    {
        string entry = AutostartEntry.BuildDesktopEntry("/opt/ELW-Meteo");

        // A desktop file with CRLF is parsed by some desktops and silently
        // ignored by others; written from Windows it would be the default.
        Assert.DoesNotContain("\r", entry, StringComparison.Ordinal);
    }

    [Fact]
    public void TheLaunchAgentRunsAtLoadButDoesNotRestart()
    {
        string plist = AutostartEntry.BuildLaunchAgent("/Applications/ELW-Meteo.app/ELW-Meteo");

        Assert.Contains("<key>RunAtLoad</key>", plist, StringComparison.Ordinal);
        // Closing the window has to mean closed.
        Assert.DoesNotContain("KeepAlive", plist, StringComparison.Ordinal);
        Assert.Contains("de.elw-meteo.autostart", plist, StringComparison.Ordinal);
    }

    [Fact]
    public void TheLaunchAgentEscapesAnAmpersandInThePath()
    {
        string plist = AutostartEntry.BuildLaunchAgent("/Users/feuerwehr & rettung/ELW-Meteo");

        Assert.Contains("feuerwehr &amp; rettung", plist, StringComparison.Ordinal);
        // An unescaped ampersand makes the plist unparseable, at which point
        // launchd ignores the file without a word.
        Assert.DoesNotContain("feuerwehr & rettung", plist, StringComparison.Ordinal);
    }

    [Fact]
    public void TheLaunchAgentIsWellFormedXml()
    {
        string plist = AutostartEntry.BuildLaunchAgent("/Users/a & b/ELW-Meteo");

        System.Xml.Linq.XDocument parsed = System.Xml.Linq.XDocument.Parse(plist);

        Assert.Equal("plist", parsed.Root!.Name.LocalName);
    }
}

public class ScreenChoiceTests
{
    private static ScreenInfo Laptop => new("eDP-1", "eDP-1", 0, 0, 1920, 1080, IsPrimary: true);

    private static ScreenInfo Vehicle => new("HDMI-1", "HDMI-1", 1920, 0, 1920, 1080, IsPrimary: false);

    [Fact]
    public void WithoutScreensThereIsNothingToChoose()
    {
        Assert.Null(ScreenChoice.Select([], "HDMI-1"));
        Assert.Null(ScreenChoice.Select(null, "HDMI-1"));
    }

    [Fact]
    public void AnEmptyPreferenceTakesThePrimaryScreen()
    {
        ScreenInfo? chosen = ScreenChoice.Select([Vehicle, Laptop], string.Empty);

        Assert.Equal("eDP-1", chosen!.Id);
    }

    [Fact]
    public void TheConfiguredScreenIsUsedWhenItIsThere()
    {
        ScreenInfo? chosen = ScreenChoice.Select([Laptop, Vehicle], "HDMI-1");

        Assert.Equal("HDMI-1", chosen!.Id);
    }

    [Fact]
    public void TheIdIsMatchedWithoutRegardForCase()
    {
        ScreenInfo? chosen = ScreenChoice.Select([Laptop, Vehicle], "hdmi-1");

        Assert.Equal("HDMI-1", chosen!.Id);
    }

    [Fact]
    public void AnUnpluggedScreenFallsBackToThePrimaryOne()
    {
        // The undocked vehicle laptop. Positioning the window at the coordinates
        // of a monitor that is not connected puts it where nobody can see it, on
        // a machine nobody can get back without editing a settings file.
        ScreenInfo? chosen = ScreenChoice.Select([Laptop], "HDMI-1");

        Assert.Equal("eDP-1", chosen!.Id);
    }

    [Fact]
    public void WithoutAPrimaryFlagTheFirstScreenIsUsed()
    {
        // Some X11 setups report no primary at all.
        ScreenInfo a = Vehicle with { Id = "DP-1" };

        ScreenInfo? chosen = ScreenChoice.Select([a, Vehicle], null);

        Assert.Equal("DP-1", chosen!.Id);
    }

    [Fact]
    public void AMissingPreferenceIsReportable()
    {
        Assert.True(ScreenChoice.IsPreferenceMissing([Laptop], "HDMI-1"));
        Assert.False(ScreenChoice.IsPreferenceMissing([Laptop, Vehicle], "HDMI-1"));
        // No preference cannot be missing, and neither can one on a machine that
        // reported no screens — that is a shell problem, not a configuration one.
        Assert.False(ScreenChoice.IsPreferenceMissing([Laptop], string.Empty));
        Assert.False(ScreenChoice.IsPreferenceMissing([], "HDMI-1"));
    }

    [Fact]
    public void TheDescriptionNamesTheResolution()
    {
        Assert.Contains("1920×1080", Laptop.Describe(), StringComparison.Ordinal);
        Assert.Contains("Hauptbildschirm", Laptop.Describe(), StringComparison.Ordinal);
        Assert.DoesNotContain("Hauptbildschirm", Vehicle.Describe(), StringComparison.Ordinal);
    }
}

public class KioskStationCatalogTests
{
    [Fact]
    public void NeitherDiagnosticsNorSettingsCanBeRotatedTo()
    {
        // A kiosk that parks itself on the settings page puts every API key and
        // serial port on display, within reach of whoever walks past.
        Assert.DoesNotContain(KioskStationCatalog.All, s => s.Title.Contains("Diagnose", StringComparison.Ordinal));
        Assert.DoesNotContain(KioskStationCatalog.All, s => s.Title.Contains("Einstellungen", StringComparison.Ordinal));
    }

    [Fact]
    public void TheTabIndicesAreDistinctAndInOrder()
    {
        int[] indices = [.. KioskStationCatalog.All.Select(s => s.TabIndex)];

        Assert.Equal(indices.Distinct().Count(), indices.Length);
        Assert.Equal([.. indices.OrderBy(i => i)], indices);
    }

    [Fact]
    public void UnknownIdsAreDropped()
    {
        IReadOnlyList<KioskStation> resolved =
            KioskStationCatalog.Resolve(["lage", "gibtsnicht", "karte"]);

        Assert.Equal(["lage", "karte"], resolved.Select(s => s.Id));
    }

    [Fact]
    public void ARepeatedIdIsListedOnlyOnce()
    {
        IReadOnlyList<KioskStation> resolved =
            KioskStationCatalog.Resolve(["lage", "lage", "karte"]);

        Assert.Equal(2, resolved.Count);
    }

    [Fact]
    public void TheStoredOrderIsKept()
    {
        IReadOnlyList<KioskStation> resolved =
            KioskStationCatalog.Resolve(["verlauf", "uhr", "lage"]);

        Assert.Equal(["verlauf", "uhr", "lage"], resolved.Select(s => s.Id));
    }

    [Fact]
    public void TheDefaultRotationResolves()
    {
        IReadOnlyList<KioskStation> resolved =
            KioskStationCatalog.Resolve(KioskStationCatalog.DefaultRotation);

        Assert.Equal(KioskStationCatalog.DefaultRotation.Count, resolved.Count);
    }

    [Fact]
    public void AnEmptySettingFallsBackToTheDefaultRotation()
    {
        AppSettings settings = new();

        Assert.NotEmpty(settings.ResolveCarouselStations());
    }

    [Fact]
    public void ARotationOfNothingButUnknownIdsFallsBackToTheDefault()
    {
        // The case that matters after an upgrade removed a station: the rotation
        // must not end up empty and silently stop working.
        AppSettings settings = new() { CarouselStationIds = ["weggefallen"] };

        Assert.NotEmpty(settings.ResolveCarouselStations());
    }
}

public class TabCarouselTests
{
    private static readonly DateTimeOffset Noon =
        new(2026, 3, 14, 12, 0, 0, TimeSpan.Zero);

    private static TabCarousel ThreeStations()
    {
        TabCarousel carousel = new();
        carousel.Configure(KioskStationCatalog.Resolve(["lage", "karte", "uhr"]));
        return carousel;
    }

    [Fact]
    public void AnEmptyRotationDoesNothing()
    {
        TabCarousel carousel = new();

        Assert.Null(carousel.Current);
        Assert.Null(carousel.Next());
        Assert.False(carousel.CanRotate);
    }

    [Fact]
    public void ASingleStationIsNotARotation()
    {
        TabCarousel carousel = new();
        carousel.Configure(KioskStationCatalog.Resolve(["lage"]));

        Assert.False(carousel.CanRotate);
        Assert.False(carousel.ShouldAdvance(Noon, null, TimeSpan.FromSeconds(60)));
    }

    [Fact]
    public void ItWrapsAround()
    {
        TabCarousel carousel = ThreeStations();

        Assert.Equal("lage", carousel.Current!.Id);
        Assert.Equal("karte", carousel.Next()!.Id);
        Assert.Equal("uhr", carousel.Next()!.Id);
        Assert.Equal("lage", carousel.Next()!.Id);
    }

    [Fact]
    public void WithNoInteractionTheNextStationIsDue()
    {
        Assert.True(ThreeStations().ShouldAdvance(Noon, null, TimeSpan.FromSeconds(60)));
    }

    [Fact]
    public void ARecentInteractionHoldsTheRotation()
    {
        TabCarousel carousel = ThreeStations();

        Assert.False(carousel.ShouldAdvance(
            Noon, Noon - TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(60)));
    }

    [Fact]
    public void TheRotationResumesByItselfAfterTheQuietStretch()
    {
        // The whole point of the grace period being a timeout rather than a
        // latch: a sleeve brushing the touchscreen must not stop the rotation
        // for the rest of the shift.
        TabCarousel carousel = ThreeStations();

        Assert.True(carousel.ShouldAdvance(
            Noon, Noon - TimeSpan.FromSeconds(61), TimeSpan.FromSeconds(60)));
    }

    [Fact]
    public void AClockThatJumpedBackwardsHoldsRatherThanRotates()
    {
        // A GPS time fix landing, or daylight saving. Holding one interval too
        // long is harmless; rotating away from somebody mid-sentence is not.
        TabCarousel carousel = ThreeStations();

        Assert.False(carousel.ShouldAdvance(
            Noon, Noon + TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(60)));
    }

    [Fact]
    public void AManualTabSwitchMovesThePosition()
    {
        TabCarousel carousel = ThreeStations();

        carousel.SyncToTab(KioskStationCatalog.ById("uhr")!.TabIndex);

        // Continues from where the operator left off, not from wherever the
        // rotation happened to be.
        Assert.Equal("uhr", carousel.Current!.Id);
        Assert.Equal("lage", carousel.Next()!.Id);
    }

    [Fact]
    public void ATabOutsideTheRotationLeavesThePositionAlone()
    {
        TabCarousel carousel = ThreeStations();
        carousel.Next();

        carousel.SyncToTab(99);

        Assert.Equal("karte", carousel.Current!.Id);
    }

    [Fact]
    public void ReconfiguringKeepsTheCurrentStation()
    {
        TabCarousel carousel = ThreeStations();
        carousel.Next();
        Assert.Equal("karte", carousel.Current!.Id);

        // Changing the dwell time must not jump the view back to the first tab.
        carousel.Configure(KioskStationCatalog.Resolve(["lage", "karte", "uhr", "verlauf"]));

        Assert.Equal("karte", carousel.Current!.Id);
    }

    [Fact]
    public void ReconfiguringWithoutTheCurrentStationStartsOver()
    {
        TabCarousel carousel = ThreeStations();
        carousel.Next();

        carousel.Configure(KioskStationCatalog.Resolve(["uhr", "verlauf"]));

        Assert.Equal("uhr", carousel.Current!.Id);
    }

    // ------------------------------------------------- the interaction clock

    private static readonly TimeSpan Grace = TimeSpan.FromSeconds(60);

    /// <summary>
    /// What the view model does for one rotation step, including the change
    /// notification that the tab switch raises on the way back in.
    /// </summary>
    private static KioskStation? TakeOneStep(TabCarousel carousel, DateTimeOffset now)
    {
        KioskStation? next = carousel.Advance(now, Grace);

        if (next is not null)
        {
            // Re-entrant: setting the selected tab raises the same notification a
            // person's click does.
            carousel.NoteInteraction(now);
            carousel.SyncToTab(next.TabIndex);
        }

        carousel.CompleteAdvance();
        return next;
    }

    [Fact]
    public void TheRotationDoesNotCountItsOwnStepAsAnInteraction()
    {
        // The bug this exists for: without the suppression the rotation registers
        // its own first step as somebody operating the app, holds, and never takes
        // a second step — a kiosk that shows one view for the rest of the shift.
        TabCarousel carousel = ThreeStations();

        Assert.Equal("karte", TakeOneStep(carousel, Noon)!.Id);
        Assert.Equal("uhr", TakeOneStep(carousel, Noon + TimeSpan.FromSeconds(30))!.Id);
        Assert.Equal("lage", TakeOneStep(carousel, Noon + TimeSpan.FromSeconds(60))!.Id);
    }

    [Fact]
    public void AnInteractionBeforeTheStepStillHoldsTheRotation()
    {
        TabCarousel carousel = ThreeStations();
        carousel.NoteInteraction(Noon);

        Assert.Null(TakeOneStep(carousel, Noon + TimeSpan.FromSeconds(10)));
        Assert.Equal("lage", carousel.Current!.Id);
    }

    [Fact]
    public void AHeldRotationResumesAfterTheQuietStretch()
    {
        TabCarousel carousel = ThreeStations();
        carousel.NoteInteraction(Noon);

        Assert.Null(TakeOneStep(carousel, Noon + TimeSpan.FromSeconds(30)));
        Assert.NotNull(TakeOneStep(carousel, Noon + TimeSpan.FromSeconds(61)));
    }

    [Fact]
    public void AHoldThatWasRefusedDoesNotLeaveTheSuppressionOpen()
    {
        // Advance returns null when it is held; if that path left IsAdvancing set,
        // every later interaction would be swallowed and the hold would never end.
        TabCarousel carousel = ThreeStations();
        carousel.NoteInteraction(Noon);

        Assert.Null(TakeOneStep(carousel, Noon + TimeSpan.FromSeconds(5)));
        Assert.False(carousel.IsAdvancing);

        carousel.NoteInteraction(Noon + TimeSpan.FromSeconds(50));

        Assert.Equal(Noon + TimeSpan.FromSeconds(50), carousel.LastInteraction);
    }

    [Fact]
    public void AManualSwitchDuringAHoldMovesThePositionWithoutRotating()
    {
        TabCarousel carousel = ThreeStations();

        carousel.NoteInteraction(Noon);
        carousel.SyncToTab(KioskStationCatalog.ById("uhr")!.TabIndex);

        Assert.Null(TakeOneStep(carousel, Noon + TimeSpan.FromSeconds(10)));
        // Continues from where the operator left off once the hold ends.
        Assert.Equal("lage", TakeOneStep(carousel, Noon + TimeSpan.FromSeconds(61))!.Id);
    }

    [Fact]
    public void AnEmptyRotationIsNeverHeld()
    {
        Assert.False(new TabCarousel().IsHeld(Noon, Grace));
    }

    [Fact]
    public void IsHeldFollowsTheInteraction()
    {
        TabCarousel carousel = ThreeStations();

        Assert.False(carousel.IsHeld(Noon, Grace));

        carousel.NoteInteraction(Noon);

        Assert.True(carousel.IsHeld(Noon + TimeSpan.FromSeconds(5), Grace));
        Assert.False(carousel.IsHeld(Noon + TimeSpan.FromSeconds(61), Grace));
    }

    [Fact]
    public void TheIntervalIsClampedToSomethingReadable()
    {
        Assert.Equal(TimeSpan.FromSeconds(5), TabCarousel.ClampInterval(0));
        Assert.Equal(TimeSpan.FromSeconds(5), TabCarousel.ClampInterval(-30));
        Assert.Equal(TimeSpan.FromSeconds(30), TabCarousel.ClampInterval(30));
        Assert.Equal(TimeSpan.FromHours(1), TabCarousel.ClampInterval(100_000));
    }
}
