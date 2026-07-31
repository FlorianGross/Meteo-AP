using ElwMeteo.Core.Assessment;
using ElwMeteo.Core.Models;
using ElwMeteo.Core.Reporting;
using Xunit;

namespace ElwMeteo.Core.Tests;

/// <summary>
/// The printed report is generated as a string, which is the only reason it can
/// be checked at all — a report that comes out wrong is not something anybody
/// notices until it is on paper.
/// </summary>
public class WeatherReportPageTests
{
    private static readonly DateTimeOffset Noon =
        new(2026, 3, 14, 12, 0, 0, TimeSpan.Zero);

    private static WeatherSnapshot Snapshot(int hours = 54)
    {
        var hourly = new List<HourlyStep>();

        for (int hour = -6; hour < hours - 6; hour++)
        {
            DateTimeOffset time = Noon.AddHours(hour);

            hourly.Add(new HourlyStep(
                time,
                TemperatureC: 8 + (4 * Math.Sin(hour / 6.0)),
                PrecipitationMm: hour % 7 == 0 ? 1.4 : 0,
                PrecipitationProbabilityPercent: 30,
                WindSpeedMs: 6,
                WindGustMs: 11,
                WindDirectionDeg: 245,
                CloudCoverPercent: 70,
                RelativeHumidityPercent: 78,
                VisibilityM: 14000,
                CapeJkg: 40,
                WeatherCode: 61));
        }

        return new WeatherSnapshot
        {
            Position = new GeoPosition(50.1109, 8.6821, PositionSource.Gps, Noon, AccuracyM: 4),
            RetrievedAtUtc = Noon,
            TemperatureC = 8.2,
            ApparentTemperatureC = 5.4,
            DewPointC = 4.6,
            RelativeHumidityPercent = 78,
            PressureMslHpa = 1004,
            WindSpeedMs = 6.2,
            WindGustMs = 11.4,
            WindDirectionDeg = 245,
            CloudCoverPercent = 70,
            VisibilityM = 14000,
            PrecipitationMm = 0.4,
            WeatherCode = 61,
            ElevationM = 112,
            ModelName = "ICON-D2",
            Hourly = hourly,
            Daily =
            [
                new DailySummary(Noon.Date, 3.0, 11.0, 4.2, 2.0, 14.0),
                new DailySummary(Noon.Date.AddDays(1), 2.0, 9.5, 1.1, 3.0, 18.0)
            ]
        };
    }

    private static TacticalAssessment Assess(WeatherSnapshot? snapshot = null) =>
        WeatherAssessor.Assess(snapshot ?? Snapshot(), Noon);

    private static string Build(ReportOptions? options = null) =>
        WeatherReportPage.Build(Assess(), Noon, options);

    // ------------------------------------------------------------ structure

    [Fact]
    public void ThePageIsAWholeHtmlDocument()
    {
        string html = Build();

        Assert.StartsWith("<!DOCTYPE html>", html, StringComparison.Ordinal);
        Assert.EndsWith("</html>\r\n", html.ReplaceLineEndings("\r\n"), StringComparison.Ordinal);
        Assert.Contains("<meta charset=\"utf-8\" />", html, StringComparison.Ordinal);
        Assert.Contains("lang=\"de\"", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Self-contained on purpose: no stylesheet to lose and nothing fetched at
    /// print time, because the machine printing it may have no network.
    /// </summary>
    [Fact]
    public void NothingIsLoadedFromTheNetwork()
    {
        string html = Build();

        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("http://", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<link", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<img", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ItIsLaidOutForA4()
    {
        Assert.Contains("@page { size: A4 portrait;", Build(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The application's dark theme printed out is both unreadable and a
    /// cartridge's worth of toner.
    /// </summary>
    [Fact]
    public void ItPrintsBlackOnWhite()
    {
        Assert.Contains("color: #111; background: #fff;", Build(), StringComparison.Ordinal);
    }

    [Fact]
    public void EverySectionIsPresent()
    {
        string html = Build();

        Assert.Contains("Aktuelle Lage", html, StringComparison.Ordinal);
        Assert.Contains("Ausbreitung und Windentwicklung", html, StringComparison.Ordinal);
        Assert.Contains("Verlauf der nächsten 48 Stunden", html, StringComparison.Ordinal);
        Assert.Contains("Tagesübersicht", html, StringComparison.Ordinal);
        Assert.Contains("Sonne und Mond", html, StringComparison.Ordinal);
    }

    [Fact]
    public void TheMeasuredValuesAreOnTheSheet()
    {
        string html = Build();

        Assert.Contains("8,2 °C", html, StringComparison.Ordinal);
        Assert.Contains("Taupunkt", html, StringComparison.Ordinal);
        Assert.Contains("1004 hPa", html, StringComparison.Ordinal);
        Assert.Contains("ICON-D2", html, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSourcesAreCreditedInTheFooter()
    {
        string html = Build();

        Assert.Contains("Deutscher Wetterdienst", html, StringComparison.Ordinal);
        Assert.Contains("Bevölkerungsschutz", html, StringComparison.Ordinal);
    }

    [Fact]
    public void TheIncidentLabelAndOrganisationAppearWhenGiven()
    {
        string html = Build(new ReportOptions
        {
            IncidentLabel = "Brand 3, Musterstraße 7",
            Organisation = "Feuerwache Mitte"
        });

        Assert.Contains("Brand 3, Musterstraße 7", html, StringComparison.Ordinal);
        Assert.Contains("Feuerwache Mitte", html, StringComparison.Ordinal);
    }

    // -------------------------------------------------------------- chart

    [Fact]
    public void TheChartIsDrawnAsInlineVectorGraphics()
    {
        string html = Build();

        Assert.Contains("<svg viewBox=\"0 0 1000 260\"", html, StringComparison.Ordinal);
        Assert.Contains("class=\"temperature\"", html, StringComparison.Ordinal);
        Assert.Contains("class=\"dewpoint\"", html, StringComparison.Ordinal);
        Assert.Contains("class=\"rain\"", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// A blank set of axes would suggest the data was flat rather than absent.
    /// </summary>
    [Fact]
    public void WithoutHourlyDataNoChartIsDrawnAtAll()
    {
        WeatherSnapshot bare = Snapshot() with { Hourly = [] };
        string html = WeatherReportPage.Build(Assess(bare), Noon);

        Assert.DoesNotContain("<svg", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Verlauf der nächsten 48 Stunden", html, StringComparison.Ordinal);
    }

    /// <summary>The two curves must be distinguishable on a monochrome printer.</summary>
    [Fact]
    public void TheTwoCurvesDifferInDashPatternNotOnlyInColour()
    {
        Assert.Contains(".chart .dewpoint { fill: none; stroke: #17795e; stroke-width: 1.6; stroke-dasharray: 6 3; }",
            Build(), StringComparison.Ordinal);
    }

    [Fact]
    public void TheNowMarkerIsDrawn()
    {
        Assert.Contains("class=\"now\"", Build(), StringComparison.Ordinal);
        Assert.Contains(">jetzt<", Build(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Coordinates are written with an invariant decimal point. A German comma
    /// in an SVG path is not a separator, it is a syntax error, and the whole
    /// chart silently fails to render.
    /// </summary>
    [Fact]
    public void ChartCoordinatesUseADecimalPointRegardlessOfCulture()
    {
        System.Globalization.CultureInfo previous = System.Globalization.CultureInfo.CurrentCulture;

        try
        {
            System.Globalization.CultureInfo.CurrentCulture =
                System.Globalization.CultureInfo.GetCultureInfo("de-DE");

            string html = Build();
            int start = html.IndexOf("<polyline points=\"", StringComparison.Ordinal) + 18;
            string points = html[start..html.IndexOf('"', start)];

            // "612.4,88.1 618.9,86.0" — pairs separated by spaces, never "612,4".
            foreach (string pair in points.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                Assert.Equal(2, pair.Split(',').Length);
            }
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    // ----------------------------------------------------------- warnings

    private static DwdWarning Storm => new(
        "STURMBÖEN", "Amtliche Warnung vor STURMBÖEN", WarningLevel.Severe,
        Noon, Noon.AddHours(6), "Stadt Frankfurt am Main",
        "Böen bis 90 km/h aus Südwest.", "Aufenthalt im Freien meiden.");

    [Fact]
    public void WarningsArePrintedWithLevelPeriodAndInstruction()
    {
        string html = Build(new ReportOptions { Warnings = [Storm], WarningSource = "DWD über Bright Sky" });

        Assert.Contains("Amtliche Warnung vor STURMBÖEN", html, StringComparison.Ordinal);
        Assert.Contains("Stufe 3", html, StringComparison.Ordinal);
        Assert.Contains("Aufenthalt im Freien meiden.", html, StringComparison.Ordinal);
        Assert.Contains("DWD über Bright Sky", html, StringComparison.Ordinal);
        Assert.Contains("#E1002F", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// The heading, not the wording: the footer disclaimer legitimately says
    /// „amtliche Warnungen … haben Vorrang" on every sheet.
    /// </summary>
    [Fact]
    public void WithoutWarningsTheSectionIsLeftOut()
    {
        string html = Build();

        Assert.DoesNotContain("class=\"block warnings\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"warning\"", html, StringComparison.Ordinal);
    }

    /// <summary>A warning must not be split across two sheets.</summary>
    [Fact]
    public void WarningsAreKeptOffPageBreaks()
    {
        Assert.Contains("page-break-inside: avoid", Build(), StringComparison.Ordinal);
    }

    // ------------------------------------------------------------ escaping

    /// <summary>
    /// Warning descriptions come off the network and this page is opened in a
    /// real browser. A temperature is not attacker-adjacent input; a MoWaS
    /// description is.
    /// </summary>
    [Fact]
    public void TextFromTheNetworkIsEscaped()
    {
        var hostile = new DwdWarning(
            "X",
            "<script>alert('x')</script>",
            WarningLevel.Severe,
            Noon,
            null,
            "\"Region\"",
            "Ein & Zeichen <b>fett</b>",
            null);

        string html = Build(new ReportOptions { Warnings = [hostile] });

        Assert.DoesNotContain("<script>alert", html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
        Assert.Contains("Ein &amp; Zeichen", html, StringComparison.Ordinal);
        Assert.Contains("&lt;b&gt;fett&lt;/b&gt;", html, StringComparison.Ordinal);
        Assert.Contains("&quot;Region&quot;", html, StringComparison.Ordinal);
    }

    [Fact]
    public void TheIncidentLabelIsEscapedToo()
    {
        string html = Build(new ReportOptions { IncidentLabel = "<img src=x onerror=alert(1)>" });

        Assert.DoesNotContain("<img", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("&lt;img", html, StringComparison.Ordinal);
    }

    [Fact]
    public void TheAddressIsEscapedToo()
    {
        string html = Build(new ReportOptions { AddressLine = "Straße <b>7</b>" });

        Assert.Contains("Straße &lt;b&gt;7&lt;/b&gt;", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("a & b", "a &amp; b")]
    [InlineData("<x>", "&lt;x&gt;")]
    [InlineData("\"q\"", "&quot;q&quot;")]
    [InlineData("it's", "it&#39;s")]
    [InlineData(null, "")]
    public void EscapingCoversEveryDangerousCharacter(string? input, string expected)
    {
        Assert.Equal(expected, WeatherReportPage.Escape(input));
    }

    /// <summary>Ampersands must not be double-escaped into &amp;amp;lt;.</summary>
    [Fact]
    public void EscapingIsNotAppliedTwice()
    {
        Assert.Equal("&lt;b&gt;", WeatherReportPage.Escape("<b>"));
    }

    // ------------------------------------------------------------- offline

    /// <summary>
    /// A printed sheet outlives the session that produced it. One that does not
    /// say how old its numbers are is a trap.
    /// </summary>
    [Fact]
    public void AReportFromStoredDataSaysSoOnTheSheet()
    {
        string html = Build(new ReportOptions { OfflineAge = TimeSpan.FromMinutes(41) });

        Assert.Contains("gespeicherten Stand", html, StringComparison.Ordinal);
        Assert.Contains("vor 41 Minuten", html, StringComparison.Ordinal);
    }

    [Fact]
    public void ALiveReportCarriesNoOfflineBanner()
    {
        Assert.DoesNotContain("gespeicherten Stand", Build(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, "gerade eben")]
    [InlineData(41, "vor 41 Minuten")]
    [InlineData(125, "vor 2 Stunden 5 Minuten")]
    public void TheOfflineAgeIsWorded(int minutes, string expected)
    {
        Assert.Equal(expected, WeatherReportPage.DescribeAge(TimeSpan.FromMinutes(minutes)));
    }
}
