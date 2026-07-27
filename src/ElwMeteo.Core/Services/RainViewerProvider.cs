using System.Globalization;
using System.Text.Json;

namespace ElwMeteo.Core.Services;

/// <summary>One radar composite frame, past or forecast.</summary>
public sealed record RadarFrame(DateTimeOffset Time, string Path, bool IsForecast)
{
    public string TimeLabel => Time.ToLocalTime().ToString("HH:mm");

    /// <summary>Signed offset from "now", used for the "+20 min" style labels.</summary>
    public string RelativeLabel(DateTimeOffset now)
    {
        int minutes = (int)Math.Round((Time - now).TotalMinutes);
        return minutes switch
        {
            0 => "jetzt",
            > 0 => $"+{minutes} min",
            _ => $"{minutes} min"
        };
    }
}

/// <summary>One of RainViewer's radar colour ramps.</summary>
public sealed record RadarColourScheme(int Id, string Title);

/// <summary>The animation timeline: observed frames followed by the nowcast.</summary>
public sealed record RadarTimeline(
    string TileHost,
    IReadOnlyList<RadarFrame> Frames,
    IReadOnlyList<RadarFrame> SatelliteFrames)
{
    public static RadarTimeline Empty { get; } = new(string.Empty, [], []);

    public IEnumerable<RadarFrame> Past => Frames.Where(f => !f.IsForecast);

    public IEnumerable<RadarFrame> Forecast => Frames.Where(f => f.IsForecast);

    /// <summary>
    /// The colour ramps RainViewer publishes. "Universal Blue" is the default
    /// because it stays readable over both the light and the dark base maps.
    /// </summary>
    public static IReadOnlyList<RadarColourScheme> ColourSchemes { get; } =
    [
        new(0, "Schwarz-Weiß"),
        new(1, "Original"),
        new(2, "Universal Blue"),
        new(3, "TITAN"),
        new(4, "Weather Channel"),
        new(5, "Meteored"),
        new(6, "NEXRAD Level III"),
        new(7, "Rainbow SELEX-SI"),
        new(8, "Dark Sky")
    ];

    /// <summary>
    /// Tile URL template for a frame, with {z}/{x}/{y} left in place for Leaflet.
    /// </summary>
    /// <param name="frame">Frame to render.</param>
    /// <param name="colourScheme">RainViewer colour scheme id.</param>
    /// <param name="smooth">Interpolate between radar pixels.</param>
    /// <param name="showSnow">Render snow in a separate colour.</param>
    public string TileUrlTemplate(RadarFrame frame, int colourScheme = 4, bool smooth = true, bool showSnow = true)
    {
        int smoothFlag = smooth ? 1 : 0;
        int snowFlag = showSnow ? 1 : 0;
        return $"{TileHost}{frame.Path}/512/{{z}}/{{x}}/{{y}}/{colourScheme}/{smoothFlag}_{snowFlag}.png";
    }

    /// <summary>
    /// Infrared satellite tiles. These are cloud-top temperatures, so unlike the
    /// radar they still show the cloud field at night and over gaps in radar
    /// coverage. Colour scheme 0 is the only one defined for satellite.
    /// </summary>
    public string SatelliteTileUrlTemplate(RadarFrame frame) =>
        $"{TileHost}{frame.Path}/512/{{z}}/{{x}}/{{y}}/0/0_0.png";

    /// <summary>Satellite frame closest in time to a radar frame, for a synchronised loop.</summary>
    public RadarFrame? SatelliteFrameNear(DateTimeOffset time)
    {
        if (SatelliteFrames.Count == 0)
        {
            return null;
        }

        return SatelliteFrames
            .OrderBy(frame => Math.Abs((frame.Time - time).TotalSeconds))
            .First();
    }
}

/// <summary>
/// Reads the RainViewer public radar index. It supplies roughly two hours of
/// observed composites plus a 30-minute nowcast in 10-minute steps — the
/// "what happens in the next few minutes" view on the map tab.
/// </summary>
public sealed class RainViewerProvider(HttpClient httpClient)
{
    private const string IndexUrl = "https://api.rainviewer.com/public/weather-maps.json";

    public async Task<RadarTimeline> GetTimelineAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var stream = await httpClient.GetStreamAsync(IndexUrl, cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return Parse(document.RootElement);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            throw new RadarProviderException($"Radar-Index nicht abrufbar: {ex.Message}", ex);
        }
    }

    /// <summary>Exposed for tests.</summary>
    internal static RadarTimeline Parse(JsonElement root)
    {
        string host = root.TryGetProperty("host", out JsonElement hostElement)
            ? hostElement.GetString() ?? string.Empty
            : string.Empty;

        var frames = new List<RadarFrame>();
        var satellite = new List<RadarFrame>();

        if (root.TryGetProperty("radar", out JsonElement radar))
        {
            Collect(radar, "past", isForecast: false, frames);
            Collect(radar, "nowcast", isForecast: true, frames);
        }

        // Infrared satellite is published alongside the radar in the same index.
        if (root.TryGetProperty("satellite", out JsonElement satelliteBlock))
        {
            Collect(satelliteBlock, "infrared", isForecast: false, satellite);
        }

        if (frames.Count == 0 && satellite.Count == 0)
        {
            return RadarTimeline.Empty;
        }

        return new RadarTimeline(
            host,
            frames.OrderBy(f => f.Time).ToList(),
            satellite.OrderBy(f => f.Time).ToList());

        static void Collect(JsonElement radar, string property, bool isForecast, List<RadarFrame> target)
        {
            if (!radar.TryGetProperty(property, out JsonElement array) || array.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (JsonElement item in array.EnumerateArray())
            {
                if (!item.TryGetProperty("time", out JsonElement time) ||
                    !item.TryGetProperty("path", out JsonElement path))
                {
                    continue;
                }

                if (!time.TryGetInt64(out long epochSeconds))
                {
                    continue;
                }

                string? pathValue = path.GetString();
                if (string.IsNullOrWhiteSpace(pathValue))
                {
                    continue;
                }

                target.Add(new RadarFrame(
                    DateTimeOffset.FromUnixTimeSeconds(epochSeconds),
                    pathValue,
                    isForecast));
            }
        }
    }
}

public sealed class RadarProviderException(string message, Exception? inner = null)
    : Exception(message, inner);
