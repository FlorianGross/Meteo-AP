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

/// <summary>The animation timeline: observed frames followed by the nowcast.</summary>
public sealed record RadarTimeline(string TileHost, IReadOnlyList<RadarFrame> Frames)
{
    public static RadarTimeline Empty { get; } = new(string.Empty, []);

    public IEnumerable<RadarFrame> Past => Frames.Where(f => !f.IsForecast);

    public IEnumerable<RadarFrame> Forecast => Frames.Where(f => f.IsForecast);

    /// <summary>
    /// Tile URL template for a frame, with {z}/{x}/{y} left in place for Leaflet.
    /// </summary>
    /// <param name="colourScheme">RainViewer colour scheme id; 4 is the "Universal Blue" ramp.</param>
    /// <param name="smooth">Interpolate between radar pixels.</param>
    /// <param name="showSnow">Render snow in a separate colour.</param>
    public string TileUrlTemplate(RadarFrame frame, int colourScheme = 4, bool smooth = true, bool showSnow = true)
    {
        int smoothFlag = smooth ? 1 : 0;
        int snowFlag = showSnow ? 1 : 0;
        return $"{TileHost}{frame.Path}/512/{{z}}/{{x}}/{{y}}/{colourScheme}/{smoothFlag}_{snowFlag}.png";
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

        if (!root.TryGetProperty("radar", out JsonElement radar))
        {
            return RadarTimeline.Empty;
        }

        var frames = new List<RadarFrame>();
        Collect(radar, "past", isForecast: false, frames);
        Collect(radar, "nowcast", isForecast: true, frames);

        return new RadarTimeline(host, frames.OrderBy(f => f.Time).ToList());

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
