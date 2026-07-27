using System.Globalization;
using System.Text.Json;
using ElwMeteo.Core.Models;

namespace ElwMeteo.Core.Services;

/// <summary>A place found by a name search.</summary>
public sealed record PlaceResult(string Name, string? Admin, string? Country, double Latitude, double Longitude)
{
    public string DisplayName => string.Join(", ",
        new[] { Name, Admin, Country }.Where(part => !string.IsNullOrWhiteSpace(part)));
}

/// <summary>
/// Turns place names into coordinates and coordinates back into an address.
/// Forward search uses the Open-Meteo geocoding API, reverse lookup the OSM
/// Nominatim service (which requires an identifying User-Agent — set on the
/// injected <see cref="HttpClient"/>).
/// </summary>
public sealed class GeocodingService(HttpClient httpClient)
{
    private const string SearchUrl = "https://geocoding-api.open-meteo.com/v1/search";
    private const string ReverseUrl = "https://nominatim.openstreetmap.org/reverse";

    public async Task<IReadOnlyList<PlaceResult>> SearchAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        string url = $"{SearchUrl}?name={Uri.EscapeDataString(query.Trim())}&count=8&language=de&format=json";

        try
        {
            await using var stream = await httpClient.GetStreamAsync(url, cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (!document.RootElement.TryGetProperty("results", out JsonElement results) ||
                results.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var places = new List<PlaceResult>();
            foreach (JsonElement item in results.EnumerateArray())
            {
                if (!item.TryGetProperty("latitude", out JsonElement lat) ||
                    !item.TryGetProperty("longitude", out JsonElement lon))
                {
                    continue;
                }

                places.Add(new PlaceResult(
                    item.TryGetProperty("name", out JsonElement name) ? name.GetString() ?? query : query,
                    item.TryGetProperty("admin1", out JsonElement admin) ? admin.GetString() : null,
                    item.TryGetProperty("country", out JsonElement country) ? country.GetString() : null,
                    lat.GetDouble(),
                    lon.GetDouble()));
            }

            return places;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            throw new GeocodingException($"Ortssuche fehlgeschlagen: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Best-effort address for a position. Returns null instead of throwing —
    /// a missing street name must never stop the weather panel from updating.
    /// </summary>
    public async Task<string?> ReverseAsync(GeoPosition position, CancellationToken cancellationToken = default)
    {
        string url = $"{ReverseUrl}?format=jsonv2&zoom=17&accept-language=de" +
                     $"&lat={position.Latitude.ToString("F6", CultureInfo.InvariantCulture)}" +
                     $"&lon={position.Longitude.ToString("F6", CultureInfo.InvariantCulture)}";

        try
        {
            await using var stream = await httpClient.GetStreamAsync(url, cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return BuildAddressLine(document.RootElement);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            return null;
        }
    }

    /// <summary>
    /// Assembles a short "street, postcode place" line, which is what belongs on
    /// an operations board — not Nominatim's full display_name with country and county.
    /// </summary>
    internal static string? BuildAddressLine(JsonElement root)
    {
        if (!root.TryGetProperty("address", out JsonElement address))
        {
            return root.TryGetProperty("display_name", out JsonElement display) ? display.GetString() : null;
        }

        string? street = First(address, "road", "pedestrian", "footway", "path");
        string? number = First(address, "house_number");
        string? place = First(address, "city", "town", "village", "municipality", "hamlet", "suburb");
        string? postcode = First(address, "postcode");
        string? district = First(address, "city_district", "suburb", "neighbourhood");

        var parts = new List<string>();

        if (street is not null)
        {
            parts.Add(number is not null ? $"{street} {number}" : street);
        }

        string locality = string.Join(' ', new[] { postcode, place }.Where(p => p is not null));
        if (locality.Length > 0)
        {
            parts.Add(locality);
        }

        // Only add the district when it adds information beyond the place name.
        if (district is not null && !string.Equals(district, place, StringComparison.OrdinalIgnoreCase))
        {
            parts.Add($"({district})");
        }

        if (parts.Count == 0)
        {
            return root.TryGetProperty("display_name", out JsonElement display) ? display.GetString() : null;
        }

        return string.Join(", ", parts);

        static string? First(JsonElement address, params string[] keys)
        {
            foreach (string key in keys)
            {
                if (address.TryGetProperty(key, out JsonElement element) &&
                    element.ValueKind == JsonValueKind.String)
                {
                    string? value = element.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return value.Trim();
                    }
                }
            }

            return null;
        }
    }
}

public sealed class GeocodingException(string message, Exception? inner = null)
    : Exception(message, inner);
