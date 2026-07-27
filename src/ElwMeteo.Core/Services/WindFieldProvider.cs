using System.Globalization;
using System.Text.Json;
using ElwMeteo.Core.Meteorology;
using ElwMeteo.Core.Models;

namespace ElwMeteo.Core.Services;

/// <summary>
/// Retrieves a grid of wind vectors around a position.
///
/// Open-Meteo accepts several coordinates in one request and answers with an
/// array in the same order, so a whole grid costs exactly one round trip.
/// </summary>
public sealed class WindFieldProvider(HttpClient httpClient)
{
    private const string BaseUrl = "https://api.open-meteo.com/v1/forecast";

    /// <summary>Open-Meteo caps a multi-coordinate request; stay well inside it.</summary>
    private const int MaxGridSize = 9;

    /// <summary>
    /// Builds a <paramref name="gridSize"/> × <paramref name="gridSize"/> grid centred
    /// on <paramref name="centre"/> with <paramref name="spacingMetres"/> between nodes.
    /// </summary>
    public static IReadOnlyList<LatLon> BuildGrid(LatLon centre, int gridSize, double spacingMetres)
    {
        int size = Math.Clamp(gridSize, 2, MaxGridSize);
        double spacing = Math.Max(100.0, spacingMetres);

        // Offsets run symmetrically about the centre: for size 5 that is -2..+2.
        double half = (size - 1) / 2.0;
        var points = new List<LatLon>(size * size);

        for (int row = 0; row < size; row++)
        {
            // Northing decreases as the row index grows, so the first row is the top.
            double northMetres = (half - row) * spacing;

            for (int column = 0; column < size; column++)
            {
                double eastMetres = (column - half) * spacing;

                LatLon point = centre;
                if (northMetres != 0)
                {
                    point = Geodesy.Destination(point, northMetres > 0 ? 0 : 180, Math.Abs(northMetres));
                }

                if (eastMetres != 0)
                {
                    point = Geodesy.Destination(point, eastMetres > 0 ? 90 : 270, Math.Abs(eastMetres));
                }

                points.Add(point);
            }
        }

        return points;
    }

    internal static string BuildUrl(IReadOnlyList<LatLon> grid)
    {
        string latitudes = string.Join(',',
            grid.Select(p => p.Latitude.ToString("F4", CultureInfo.InvariantCulture)));
        string longitudes = string.Join(',',
            grid.Select(p => p.Longitude.ToString("F4", CultureInfo.InvariantCulture)));

        return $"{BaseUrl}?latitude={latitudes}&longitude={longitudes}" +
               "&current=wind_speed_10m,wind_direction_10m,wind_gusts_10m" +
               "&wind_speed_unit=ms&timeformat=unixtime&timezone=UTC";
    }

    public async Task<WindField> GetAsync(
        LatLon centre,
        int gridSize,
        double spacingMetres,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<LatLon> grid = BuildGrid(centre, gridSize, spacingMetres);

        try
        {
            await using var stream = await httpClient
                .GetStreamAsync(BuildUrl(grid), cancellationToken)
                .ConfigureAwait(false);

            using var document = await JsonDocument
                .ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return Parse(document.RootElement, grid, spacingMetres);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            throw new WeatherProviderException($"Windfeld nicht abrufbar: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Turns the response into grid nodes. A single coordinate yields an object,
    /// several yield an array — both shapes are handled.
    /// </summary>
    internal static WindField Parse(JsonElement root, IReadOnlyList<LatLon> grid, double spacingMetres)
    {
        var entries = root.ValueKind == JsonValueKind.Array
            ? root.EnumerateArray().ToList()
            : [root];

        var points = new List<WindFieldPoint>(grid.Count);

        for (int i = 0; i < grid.Count; i++)
        {
            LatLon requested = grid[i];

            if (i >= entries.Count)
            {
                points.Add(new WindFieldPoint(requested.Latitude, requested.Longitude, null, null, null));
                continue;
            }

            JsonElement entry = entries[i];

            // Prefer the coordinates the service actually resolved to; they can be
            // nudged to the nearest model grid cell.
            double latitude = ReadDouble(entry, "latitude") ?? requested.Latitude;
            double longitude = ReadDouble(entry, "longitude") ?? requested.Longitude;

            if (!entry.TryGetProperty("current", out JsonElement current))
            {
                points.Add(new WindFieldPoint(latitude, longitude, null, null, null));
                continue;
            }

            points.Add(new WindFieldPoint(
                latitude,
                longitude,
                ReadDouble(current, "wind_speed_10m"),
                ReadDouble(current, "wind_gusts_10m"),
                ReadDouble(current, "wind_direction_10m")));
        }

        return new WindField(DateTimeOffset.UtcNow, spacingMetres, points);
    }

    private static double? ReadDouble(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : null;
}
