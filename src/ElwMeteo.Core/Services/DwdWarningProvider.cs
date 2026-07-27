using System.Globalization;
using System.Text.Json;
using ElwMeteo.Core.Models;

namespace ElwMeteo.Core.Services;

public interface IWarningProvider
{
    Task<IReadOnlyList<DwdWarning>> GetAsync(GeoPosition position, CancellationToken cancellationToken = default);
}

/// <summary>
/// Fetches official DWD warnings for the municipality containing a position from
/// the DWD GeoServer (https://maps.dwd.de) via WFS.
///
/// The DWD publishes this as Open Data under the GeoNutzV; the "Quelle: Deutscher
/// Wetterdienst" attribution is required and shown in the app's source line.
/// Outside Germany the query simply returns nothing.
/// </summary>
public sealed class DwdWarningProvider(HttpClient httpClient) : IWarningProvider
{
    private const string OwsEndpoint = "https://maps.dwd.de/geoserver/dwd/ows";

    /// <summary>Municipality-level warnings — the finest granularity the DWD publishes.</summary>
    private const string LayerName = "dwd:Warnungen_Gemeinden";

    public async Task<IReadOnlyList<DwdWarning>> GetAsync(
        GeoPosition position,
        CancellationToken cancellationToken = default)
    {
        // GeoServer's CQL parser reads POINT(x y) as lon/lat, but the axis order
        // for EPSG:4326 is a long-standing source of disagreement between
        // servers. Try lon/lat first and fall back to lat/lon rather than
        // silently reporting "no warnings" for the wrong reason.
        var attempts = new[]
        {
            (x: position.Longitude, y: position.Latitude),
            (x: position.Latitude, y: position.Longitude)
        };

        Exception? lastFailure = null;

        foreach (var (x, y) in attempts)
        {
            try
            {
                var warnings = await QueryAsync(x, y, cancellationToken).ConfigureAwait(false);
                if (warnings.Count > 0)
                {
                    return warnings;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
            {
                lastFailure = ex;
            }
        }

        if (lastFailure is not null)
        {
            throw new WarningProviderException($"DWD-Warnungen nicht abrufbar: {lastFailure.Message}", lastFailure);
        }

        // Both axis orders answered successfully with an empty set: genuinely no warnings.
        return [];
    }

    private async Task<List<DwdWarning>> QueryAsync(double x, double y, CancellationToken cancellationToken)
    {
        string point = string.Format(
            CultureInfo.InvariantCulture, "POINT({0:F5} {1:F5})", x, y);

        string url = $"{OwsEndpoint}?service=WFS&version=2.0.0&request=GetFeature" +
                     $"&typeName={Uri.EscapeDataString(LayerName)}" +
                     "&outputFormat=application/json" +
                     "&count=50" +
                     $"&CQL_FILTER={Uri.EscapeDataString($"INTERSECTS(the_geom,{point})")}";

        using var response = await httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return ParseFeatureCollection(document.RootElement);
    }

    /// <summary>Exposed for tests: turns a GeoJSON feature collection into warnings.</summary>
    internal static List<DwdWarning> ParseFeatureCollection(JsonElement root)
    {
        var warnings = new List<DwdWarning>();

        if (!root.TryGetProperty("features", out JsonElement features) ||
            features.ValueKind != JsonValueKind.Array)
        {
            return warnings;
        }

        foreach (JsonElement feature in features.EnumerateArray())
        {
            if (!feature.TryGetProperty("properties", out JsonElement props))
            {
                continue;
            }

            string? eventName = ReadString(props, "EVENT");
            string headline = ReadString(props, "HEADLINE") ?? eventName ?? "DWD-Warnung";

            warnings.Add(new DwdWarning(
                Event: eventName ?? headline,
                Headline: headline,
                Level: ParseLevel(props),
                Start: ReadTime(props, "SENT", "ONSET", "EFFECTIVE"),
                End: ReadTime(props, "EXPIRES"),
                RegionName: ReadString(props, "NAME") ?? ReadString(props, "AREADESC") ?? "—",
                Description: ReadString(props, "DESCRIPTION"),
                Instruction: ReadString(props, "INSTRUCTION")));
        }

        // Worst first, so the dashboard banner always shows the most serious one.
        return warnings
            .OrderByDescending(w => w.Level)
            .ThenBy(w => w.Start ?? DateTimeOffset.MaxValue)
            .ToList();
    }

    /// <summary>
    /// The layer carries a numeric SEVERITY as well as the CAP severity wording;
    /// prefer the number and fall back to the text.
    /// </summary>
    private static WarningLevel ParseLevel(JsonElement props)
    {
        foreach (string key in new[] { "LEVEL", "SEVERITY" })
        {
            if (!props.TryGetProperty(key, out JsonElement element))
            {
                continue;
            }

            if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out int numeric))
            {
                return ClampLevel(numeric);
            }

            if (element.ValueKind == JsonValueKind.String)
            {
                string? text = element.GetString();
                if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                {
                    return ClampLevel(parsed);
                }

                WarningLevel? fromWording = text?.Trim().ToLowerInvariant() switch
                {
                    "minor" => WarningLevel.Minor,
                    "moderate" => WarningLevel.Moderate,
                    "severe" => WarningLevel.Severe,
                    "extreme" => WarningLevel.Extreme,
                    _ => null
                };

                if (fromWording is not null)
                {
                    return fromWording.Value;
                }
            }
        }

        return WarningLevel.Minor;
    }

    private static WarningLevel ClampLevel(int value) => value switch
    {
        <= 0 => WarningLevel.None,
        1 => WarningLevel.Minor,
        2 => WarningLevel.Moderate,
        3 => WarningLevel.Severe,
        _ => WarningLevel.Extreme
    };

    private static string? ReadString(JsonElement props, string name) =>
        props.TryGetProperty(name, out JsonElement element) && element.ValueKind == JsonValueKind.String
            ? Blank(element.GetString())
            : null;

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static DateTimeOffset? ReadTime(JsonElement props, params string[] names)
    {
        foreach (string name in names)
        {
            string? raw = ReadString(props, name);
            if (raw is null)
            {
                continue;
            }

            if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTimeOffset parsed))
            {
                return parsed;
            }
        }

        return null;
    }
}

public sealed class WarningProviderException(string message, Exception? inner = null)
    : Exception(message, inner);
