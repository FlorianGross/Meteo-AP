using System.Text.Json;
using ElwMeteo.Core.Models;

namespace ElwMeteo.Core.Services;

/// <summary>
/// Last-resort position source: city-level coordinates derived from the public IP
/// address. Behind a cellular router this can be tens of kilometres off, so the
/// UI always labels it as imprecise and invites the operator to correct it.
/// </summary>
public sealed class IpLocationProvider(HttpClient httpClient)
{
    private const string Endpoint = "https://ipapi.co/json/";

    public async Task<GeoPosition?> GetAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var stream = await httpClient.GetStreamAsync(Endpoint, cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            JsonElement root = document.RootElement;

            if (!root.TryGetProperty("latitude", out JsonElement lat) ||
                !root.TryGetProperty("longitude", out JsonElement lon) ||
                !lat.TryGetDouble(out double latitude) ||
                !lon.TryGetDouble(out double longitude))
            {
                return null;
            }

            string? city = root.TryGetProperty("city", out JsonElement cityElement) ? cityElement.GetString() : null;

            return new GeoPosition(
                latitude,
                longitude,
                PositionSource.IpLookup,
                DateTimeOffset.UtcNow,
                // Deliberately pessimistic: this is a city centroid, not a fix.
                AccuracyM: 15_000,
                Description: city);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            return null;
        }
    }
}
