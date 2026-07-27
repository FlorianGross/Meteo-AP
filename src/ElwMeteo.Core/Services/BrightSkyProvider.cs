using System.Globalization;
using System.Text.Json;
using ElwMeteo.Core.Meteorology;
using ElwMeteo.Core.Models;

namespace ElwMeteo.Core.Services;

/// <summary>
/// A measurement from the nearest DWD station — as opposed to a model value
/// interpolated to the incident position.
/// </summary>
public sealed record StationObservation(
    string StationName,
    double DistanceM,
    DateTimeOffset Timestamp,
    double? TemperatureC,
    double? DewPointC,
    double? RelativeHumidityPercent,
    double? PressureMslHpa,
    double? WindSpeedMs,
    double? WindGustMs,
    double? WindDirectionDeg,
    double? VisibilityM,
    double? CloudCoverPercent,
    double? Precipitation10Mm)
{
    public string DistanceLabel => DistanceM >= 1000
        ? $"{DistanceM / 1000.0:F1} km"
        : $"{DistanceM:F0} m";

    /// <summary>
    /// A station tens of kilometres away says little about the incident site.
    /// The UI marks that rather than presenting it as a local measurement.
    /// </summary>
    public bool IsRepresentative => DistanceM <= 25_000;

    public TimeSpan AgeAt(DateTimeOffset now) => now - Timestamp;
}

/// <summary>One 5-minute step of the DWD radar composite at a single point.</summary>
public sealed record RadarPointStep(DateTimeOffset Time, double MillimetresPerHour, bool IsForecast)
{
    public bool HasPrecipitation => MillimetresPerHour > 0.05;
}

/// <summary>
/// Precipitation over time at one position, read straight out of the DWD radar
/// composite: measurements up to now, then the RV extrapolation ahead.
/// </summary>
public sealed record RadarPointSeries(IReadOnlyList<RadarPointStep> Steps)
{
    public static RadarPointSeries Empty { get; } = new([]);

    public bool IsEmpty => Steps.Count == 0;

    public double PeakMillimetresPerHour => Steps.Count == 0 ? 0 : Steps.Max(s => s.MillimetresPerHour);

    /// <summary>Value closest to the given instant, which is what "now" means here.</summary>
    public RadarPointStep? At(DateTimeOffset instant) =>
        Steps.Count == 0
            ? null
            : Steps.MinBy(s => Math.Abs((s.Time - instant).TotalSeconds));

    /// <summary>When precipitation starts, looking only at the forecast steps.</summary>
    public RadarPointStep? FirstWetForecast =>
        Steps.FirstOrDefault(s => s.IsForecast && s.HasPrecipitation);
}

/// <summary>
/// Client for Bright Sky (https://brightsky.dev), a free JSON front end to the
/// DWD's Open Data.
///
/// It adds three things the model-based provider cannot:
///   • measurements from real DWD stations, not interpolated model output,
///   • the official DWD warnings in German — the same CAP feed behind WarnWetter,
///   • RADOLAN radar values at a point, including the RV extrapolation ahead.
///
/// No key, no registration; the underlying data is DWD Open Data under GeoNutzV.
/// </summary>
public sealed class BrightSkyProvider(HttpClient httpClient)
{
    private const string BaseUrl = "https://api.brightsky.dev";

    /// <summary>Bright Sky reports wind in km/h by default; the app works in m/s.</summary>
    private static double? KmhToMs(double? kmh) => kmh is null ? null : WindScale.KmhToMs(kmh.Value);

    // ------------------------------------------------------------- station

    /// <summary>
    /// Current conditions from the nearest reporting DWD station. Returns null
    /// rather than throwing — a missing station reading must never hold up the
    /// model-based panel that the app is built around.
    /// </summary>
    public async Task<StationObservation?> GetCurrentStationAsync(
        GeoPosition position,
        CancellationToken cancellationToken = default)
    {
        string url = $"{BaseUrl}/current_weather" +
                     $"?lat={position.Latitude.ToString("F5", CultureInfo.InvariantCulture)}" +
                     $"&lon={position.Longitude.ToString("F5", CultureInfo.InvariantCulture)}";

        try
        {
            await using var stream = await httpClient.GetStreamAsync(url, cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return ParseCurrentWeather(document.RootElement);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            return null;
        }
    }

    internal static StationObservation? ParseCurrentWeather(JsonElement root)
    {
        if (!root.TryGetProperty("weather", out JsonElement weather) ||
            weather.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        // The first source is the one the reading came from.
        string stationName = "DWD-Station";
        double distance = 0;

        if (root.TryGetProperty("sources", out JsonElement sources) &&
            sources.ValueKind == JsonValueKind.Array &&
            sources.GetArrayLength() > 0)
        {
            JsonElement source = sources[0];
            stationName = ReadString(source, "station_name") ?? stationName;
            distance = ReadDouble(source, "distance") ?? 0;
        }

        DateTimeOffset? timestamp = ReadTime(weather, "timestamp");
        if (timestamp is null)
        {
            return null;
        }

        return new StationObservation(
            stationName,
            distance,
            timestamp.Value,
            ReadDouble(weather, "temperature"),
            ReadDouble(weather, "dew_point"),
            ReadDouble(weather, "relative_humidity"),
            ReadDouble(weather, "pressure_msl"),
            // The "_10" variants are the 10-minute means, the freshest available.
            KmhToMs(ReadDouble(weather, "wind_speed_10") ?? ReadDouble(weather, "wind_speed")),
            KmhToMs(ReadDouble(weather, "wind_gust_speed_10") ?? ReadDouble(weather, "wind_gust_speed")),
            ReadDouble(weather, "wind_direction_10") ?? ReadDouble(weather, "wind_direction"),
            ReadDouble(weather, "visibility"),
            ReadDouble(weather, "cloud_cover"),
            ReadDouble(weather, "precipitation_10"));
    }

    // -------------------------------------------------------------- alerts

    /// <summary>
    /// Official DWD warnings for the position — the same CAP feed that drives the
    /// WarnWetter app, with the German texts already in place.
    /// </summary>
    public async Task<IReadOnlyList<DwdWarning>> GetAlertsAsync(
        GeoPosition position,
        CancellationToken cancellationToken = default)
    {
        string url = $"{BaseUrl}/alerts" +
                     $"?lat={position.Latitude.ToString("F5", CultureInfo.InvariantCulture)}" +
                     $"&lon={position.Longitude.ToString("F5", CultureInfo.InvariantCulture)}";

        try
        {
            await using var stream = await httpClient.GetStreamAsync(url, cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return ParseAlerts(document.RootElement);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            throw new WarningProviderException($"DWD-Warnungen (Bright Sky) nicht abrufbar: {ex.Message}", ex);
        }
    }

    internal static List<DwdWarning> ParseAlerts(JsonElement root)
    {
        var warnings = new List<DwdWarning>();

        if (!root.TryGetProperty("alerts", out JsonElement alerts) ||
            alerts.ValueKind != JsonValueKind.Array)
        {
            return warnings;
        }

        // The location block names the warn cell the alerts belong to.
        string region = "—";
        if (root.TryGetProperty("location", out JsonElement location))
        {
            region = ReadString(location, "name")
                     ?? ReadString(location, "name_short")
                     ?? ReadString(location, "district")
                     ?? region;
        }

        foreach (JsonElement alert in alerts.EnumerateArray())
        {
            string? headline = ReadString(alert, "headline_de") ?? ReadString(alert, "headline_en");
            string? eventName = ReadString(alert, "event_de") ?? ReadString(alert, "event_en");

            warnings.Add(new DwdWarning(
                Event: eventName ?? headline ?? "DWD-Warnung",
                Headline: headline ?? eventName ?? "DWD-Warnung",
                Level: ParseSeverity(ReadString(alert, "severity")),
                Start: ReadTime(alert, "onset") ?? ReadTime(alert, "effective"),
                End: ReadTime(alert, "expires"),
                RegionName: region,
                Description: ReadString(alert, "description_de") ?? ReadString(alert, "description_en"),
                Instruction: ReadString(alert, "instruction_de") ?? ReadString(alert, "instruction_en")));
        }

        return warnings
            .OrderByDescending(w => w.Level)
            .ThenBy(w => w.Start ?? DateTimeOffset.MaxValue)
            .ToList();
    }

    /// <summary>CAP severity wording as the DWD publishes it.</summary>
    private static WarningLevel ParseSeverity(string? severity) => severity?.Trim().ToLowerInvariant() switch
    {
        "minor" => WarningLevel.Minor,
        "moderate" => WarningLevel.Moderate,
        "severe" => WarningLevel.Severe,
        "extreme" => WarningLevel.Extreme,
        _ => WarningLevel.Minor
    };

    // --------------------------------------------------------------- radar

    /// <summary>
    /// Precipitation at the position straight from the DWD radar composite, in
    /// five-minute steps. Bright Sky returns a small grid around the point plus
    /// the fractional pixel the coordinates land on, so the value can be read at
    /// the incident rather than averaged over a region.
    /// </summary>
    /// <param name="distanceMetres">Half-extent of the returned grid.</param>
    public async Task<RadarPointSeries> GetRadarSeriesAsync(
        GeoPosition position,
        double distanceMetres = 2000,
        CancellationToken cancellationToken = default)
    {
        string url = $"{BaseUrl}/radar" +
                     $"?lat={position.Latitude.ToString("F5", CultureInfo.InvariantCulture)}" +
                     $"&lon={position.Longitude.ToString("F5", CultureInfo.InvariantCulture)}" +
                     $"&distance={distanceMetres.ToString("F0", CultureInfo.InvariantCulture)}" +
                     "&format=plain";

        try
        {
            await using var stream = await httpClient.GetStreamAsync(url, cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return ParseRadar(document.RootElement, DateTimeOffset.UtcNow);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            throw new RadarProviderException($"DWD-Radarwerte nicht abrufbar: {ex.Message}", ex);
        }
    }

    internal static RadarPointSeries ParseRadar(JsonElement root, DateTimeOffset now)
    {
        if (!root.TryGetProperty("radar", out JsonElement frames) ||
            frames.ValueKind != JsonValueKind.Array)
        {
            return RadarPointSeries.Empty;
        }

        // Fractional pixel the requested coordinates fall on within the grid.
        int column = 0;
        int row = 0;
        if (root.TryGetProperty("latlon_position", out JsonElement pixel))
        {
            column = (int)Math.Round(ReadDouble(pixel, "x") ?? 0);
            row = (int)Math.Round(ReadDouble(pixel, "y") ?? 0);
        }

        var steps = new List<RadarPointStep>();

        foreach (JsonElement frame in frames.EnumerateArray())
        {
            DateTimeOffset? time = ReadTime(frame, "timestamp");
            if (time is null)
            {
                continue;
            }

            if (!frame.TryGetProperty("precipitation_5", out JsonElement grid) ||
                grid.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            double? hundredthsOfMm = ReadGridValue(grid, row, column);
            if (hundredthsOfMm is null)
            {
                continue;
            }

            // Grid values are hundredths of a millimetre per five minutes;
            // twelve five-minute steps make an hour.
            double millimetresPerHour = hundredthsOfMm.Value * 0.01 * 12.0;

            steps.Add(new RadarPointStep(
                time.Value,
                millimetresPerHour,
                // Anything ahead of the request is the RV extrapolation.
                time.Value > now.AddMinutes(2)));
        }

        return new RadarPointSeries(steps.OrderBy(s => s.Time).ToList());
    }

    /// <summary>Reads one cell, clamping to the grid so an edge request still works.</summary>
    private static double? ReadGridValue(JsonElement grid, int row, int column)
    {
        int rows = grid.GetArrayLength();
        if (rows == 0)
        {
            return null;
        }

        JsonElement rowElement = grid[Math.Clamp(row, 0, rows - 1)];
        if (rowElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        int columns = rowElement.GetArrayLength();
        if (columns == 0)
        {
            return null;
        }

        JsonElement cell = rowElement[Math.Clamp(column, 0, columns - 1)];
        return cell.ValueKind == JsonValueKind.Number ? cell.GetDouble() : null;
    }

    // ---------------------------------------------------------------- utils

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? Blank(value.GetString())
            : null;

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static double? ReadDouble(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : null;

    private static DateTimeOffset? ReadTime(JsonElement element, string name)
    {
        string? raw = ReadString(element, name);

        return raw is not null &&
               DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset parsed)
            ? parsed
            : null;
    }
}
