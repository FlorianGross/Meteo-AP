using System.Text.Json;
using ElwMeteo.Core.Models;
using ElwMeteo.Core.Services;
using Xunit;

namespace ElwMeteo.Core.Tests;

/// <summary>
/// Tests against recorded response shapes. The federal warning service is not
/// reachable from the build environment, so what is pinned here is the parsing:
/// the shapes come from the published OpenAPI description and the CAP profile
/// the service follows.
/// </summary>
public class NinaWarningTests
{
    private static JsonElement Parse(string json) =>
        JsonDocument.Parse(json).RootElement.Clone();

    // ------------------------------------------------------ regional key

    [Theory]
    [InlineData("055150000000", "055150000000")]
    [InlineData("05515000", "055150000000")]
    [InlineData("05515", "055150000000")]
    [InlineData("05 515 000", "055150000000")]
    public void AKeyIsBroughtToDistrictLevel(string input, string expected)
    {
        Assert.Equal(expected, NinaWarningProvider.NormaliseArs(input));
    }

    /// <summary>
    /// The published list is municipality-level, but the dashboard is only
    /// served per district. Passing a municipality key straight through answers
    /// 404, which would read as „no warnings" — the exact failure this feature
    /// exists to prevent.
    /// </summary>
    [Fact]
    public void AMunicipalityKeyIsReducedToItsDistrict()
    {
        Assert.Equal("064340000000", NinaWarningProvider.NormaliseArs("064340012012"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("abcd")]
    [InlineData("055")]
    public void AnUnusableKeyBecomesEmptyRatherThanWrong(string? input)
    {
        Assert.Equal(string.Empty, NinaWarningProvider.NormaliseArs(input));
    }

    // -------------------------------------------------------- region list

    [Fact]
    public void TheRegionListIsReadFromThePairShape()
    {
        var regions = NinaWarningProvider.ParseRegions(Parse("""
            { "daten": [
                ["055150000000", "Essen, Stadt"],
                ["064340000000", "Hochtaunuskreis"]
            ] }
            """));

        Assert.Equal(2, regions.Count);
        Assert.Equal("Essen, Stadt", regions[0].Name);
        Assert.Equal("055150000000", regions[0].Ars);
    }

    [Fact]
    public void TheRegionListIsAlsoReadFromTheObjectShape()
    {
        var regions = NinaWarningProvider.ParseRegions(Parse("""
            [ { "code": "064340000000", "name": "Hochtaunuskreis" } ]
            """));

        Assert.Single(regions);
        Assert.Equal("Hochtaunuskreis", regions[0].Name);
    }

    [Fact]
    public void RowsWithoutAUsableKeyAreDropped()
    {
        var regions = NinaWarningProvider.ParseRegions(Parse("""
            { "daten": [ ["", "Ohne Schlüssel"], ["064340000000", ""], ["055150000000", "Essen, Stadt"] ] }
            """));

        Assert.Single(regions);
    }

    [Fact]
    public void SearchNeedsEveryWordToMatch()
    {
        NinaRegion[] regions =
        [
            new("055150000000", "Essen, Stadt"),
            new("064340000000", "Hochtaunuskreis"),
            new("051130000000", "Essen an der Ruhr, Kreis")
        ];

        Assert.Equal(2, NinaWarningProvider.Search(regions, "essen").Count);
        Assert.Single(NinaWarningProvider.Search(regions, "essen stadt"));
        Assert.Empty(NinaWarningProvider.Search(regions, "hamburg"));
        Assert.Empty(NinaWarningProvider.Search(regions, "   "));
    }

    // ---------------------------------------------------------- dashboard

    [Fact]
    public void DashboardEntriesCarryTheirProvider()
    {
        var entries = NinaWarningProvider.ReadDashboardIdentifiers(Parse("""
            [
              { "id": "mow.DE-NW-E-SE030", "payload": { "data": { "provider": "MOWAS", "msgType": "Alert" } } },
              { "id": "dwd.EFF20260314",   "payload": { "data": { "provider": "DWD",   "msgType": "Alert" } } }
            ]
            """));

        Assert.Equal(2, entries.Count);
        Assert.Equal("MOWAS", entries[0].Provider);
        Assert.Equal("DWD", entries[1].Provider);
    }

    /// <summary>A withdrawn message must not show up as an active warning.</summary>
    [Fact]
    public void CancelledMessagesAreDropped()
    {
        var entries = NinaWarningProvider.ReadDashboardIdentifiers(Parse("""
            [
              { "id": "a", "payload": { "data": { "provider": "MOWAS", "msgType": "Cancel" } } },
              { "id": "b", "payload": { "data": { "provider": "MOWAS", "msgType": "Update" } } }
            ]
            """));

        Assert.Single(entries);
        Assert.Equal("b", entries[0].Identifier);
    }

    [Fact]
    public void AnUnexpectedShapeYieldsNothingRatherThanThrowing()
    {
        Assert.Empty(NinaWarningProvider.ReadDashboardIdentifiers(Parse("""{ "error": "nope" }""")));
        Assert.Empty(NinaWarningProvider.ReadDashboardIdentifiers(Parse("[ { \"no-id\": 1 } ]")));
    }

    /// <summary>
    /// DWD warnings already arrive from two dedicated routes that resolve to the
    /// municipality rather than the whole district. Taking them from here too
    /// would list every storm warning twice, worded differently.
    /// </summary>
    [Fact]
    public void TheWeatherServiceIsExcludedByDefault()
    {
        var provider = new NinaWarningProvider(new HttpClient());

        Assert.Contains("DWD", provider.ExcludedProviders);
        Assert.DoesNotContain("MOWAS", provider.ExcludedProviders);
    }

    // ------------------------------------------------------------ warning

    private const string HazardousMaterial = """
        {
          "identifier": "mow.DE-NW-E-SE030",
          "sender": "BBK",
          "sent": "2026-03-14T09:41:00+01:00",
          "status": "Actual",
          "msgType": "Alert",
          "info": [
            {
              "language": "DE",
              "event": "Gefahrstoffaustritt",
              "severity": "Severe",
              "urgency": "Immediate",
              "headline": "Gefahrstoffaustritt im Industriegebiet Nord",
              "description": "Aus einem Betrieb tritt Ammoniak aus.",
              "instruction": "Fenster und Türen schließen, Lüftung abschalten.",
              "onset": "2026-03-14T09:40:00+01:00",
              "expires": "2026-03-14T15:00:00+01:00",
              "area": [ { "areaDesc": "Stadt Essen, Stadtteil Nord" } ]
            }
          ]
        }
        """;

    [Fact]
    public void AMowasMessageBecomesAWarning()
    {
        DwdWarning? warning = NinaWarningProvider.ParseWarning(Parse(HazardousMaterial));

        Assert.NotNull(warning);
        Assert.Equal("Gefahrstoffaustritt", warning.Event);
        Assert.Equal(WarningLevel.Severe, warning.Level);
        Assert.Equal("Stadt Essen, Stadtteil Nord", warning.RegionName);
        Assert.Equal("Fenster und Türen schließen, Lüftung abschalten.", warning.Instruction);
        Assert.Equal(new DateTimeOffset(2026, 3, 14, 8, 40, 0, TimeSpan.Zero), warning.Start);
        Assert.Equal(new DateTimeOffset(2026, 3, 14, 14, 0, 0, TimeSpan.Zero), warning.End);
    }

    [Theory]
    [InlineData("Minor", WarningLevel.Minor)]
    [InlineData("Moderate", WarningLevel.Moderate)]
    [InlineData("Severe", WarningLevel.Severe)]
    [InlineData("Extreme", WarningLevel.Extreme)]
    [InlineData("severe", WarningLevel.Severe)]
    public void TheCapSeverityIsMapped(string severity, WarningLevel expected)
    {
        DwdWarning warning = NinaWarningProvider.ParseWarning(Parse($$"""
            { "status": "Actual", "info": [ { "event": "X", "severity": "{{severity}}" } ] }
            """))!;

        Assert.Equal(expected, warning.Level);
    }

    /// <summary>
    /// „Unknown" is common in MoWaS messages. Reading it as harmless would put a
    /// hazardous-material release below the alert threshold.
    /// </summary>
    [Fact]
    public void AnUnknownSeverityIsNotTreatedAsHarmless()
    {
        DwdWarning warning = NinaWarningProvider.ParseWarning(Parse("""
            { "status": "Actual", "info": [ { "event": "X", "severity": "Unknown" } ] }
            """))!;

        Assert.Equal(WarningLevel.Moderate, warning.Level);
    }

    [Fact]
    public void TheGermanLanguageBlockIsPreferred()
    {
        DwdWarning warning = NinaWarningProvider.ParseWarning(Parse("""
            {
              "status": "Actual",
              "info": [
                { "language": "EN-US", "event": "Fire", "severity": "Severe", "headline": "Fire" },
                { "language": "DE",    "event": "Brand", "severity": "Severe", "headline": "Großbrand" }
              ]
            }
            """))!;

        Assert.Equal("Brand", warning.Event);
        Assert.Equal("Großbrand", warning.Headline);
    }

    [Fact]
    public void WithoutAGermanBlockTheFirstOneIsUsed()
    {
        DwdWarning warning = NinaWarningProvider.ParseWarning(Parse("""
            { "status": "Actual", "info": [ { "language": "EN-US", "event": "Flood", "severity": "Severe" } ] }
            """))!;

        Assert.Equal("Flood", warning.Event);
    }

    /// <summary>
    /// Hiding a nationwide test would be confusing on a Warntag; letting it
    /// sound the alarm would be worse. So it shows, marked, below the threshold.
    /// </summary>
    [Fact]
    public void AnExerciseIsShownButHeldBelowTheAlertThreshold()
    {
        DwdWarning warning = NinaWarningProvider.ParseWarning(Parse("""
            {
              "status": "Exercise",
              "info": [ { "event": "Probealarm", "severity": "Extreme", "headline": "Bundesweiter Warntag" } ]
            }
            """))!;

        Assert.StartsWith("PROBE", warning.Event, StringComparison.Ordinal);
        Assert.Equal(WarningLevel.Minor, warning.Level);
        Assert.True(warning.Level < new WarningMonitor().Threshold);
    }

    [Fact]
    public void ACancelledMessageParsesToNothing()
    {
        Assert.Null(NinaWarningProvider.ParseWarning(Parse("""
            { "status": "Actual", "msgType": "Cancel", "info": [ { "event": "X", "severity": "Severe" } ] }
            """)));
    }

    [Fact]
    public void AMessageWithoutInfoParsesToNothing()
    {
        Assert.Null(NinaWarningProvider.ParseWarning(Parse("""{ "status": "Actual" }""")));
        Assert.Null(NinaWarningProvider.ParseWarning(Parse("""{ "status": "Actual", "info": [] }""")));
    }

    [Fact]
    public void SeveralAreasAreJoinedIntoOneLine()
    {
        DwdWarning warning = NinaWarningProvider.ParseWarning(Parse("""
            {
              "status": "Actual",
              "info": [ { "event": "X", "severity": "Severe", "area": [
                 { "areaDesc": "Essen" }, { "areaDesc": "Mülheim" } ] } ]
            }
            """))!;

        Assert.Equal("Essen, Mülheim", warning.RegionName);
    }

    [Fact]
    public void WithoutARegionTheSenderStandsIn()
    {
        DwdWarning warning = NinaWarningProvider.ParseWarning(Parse("""
            { "status": "Actual", "info": [ { "event": "X", "severity": "Severe", "senderName": "Kreis Unna" } ] }
            """))!;

        Assert.Equal("Kreis Unna", warning.RegionName);
    }

    /// <summary>Not configured is not a failure — it means the operator has not picked a region.</summary>
    [Fact]
    public async Task WithoutARegionNothingIsFetchedAndNothingThrows()
    {
        var provider = new NinaWarningProvider(new HttpClient()) { Enabled = true };
        var position = new GeoPosition(51.45, 7.01, PositionSource.Manual, DateTimeOffset.UtcNow);

        Assert.Empty(await provider.GetAsync(position));
    }

    [Fact]
    public async Task WhenSwitchedOffNothingIsFetched()
    {
        var provider = new NinaWarningProvider(new HttpClient())
        {
            Enabled = false,
            Ars = "055150000000"
        };

        var position = new GeoPosition(51.45, 7.01, PositionSource.Manual, DateTimeOffset.UtcNow);

        Assert.Empty(await provider.GetAsync(position));
    }
}
