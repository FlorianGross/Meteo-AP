using System.Diagnostics;

namespace ElwMeteo.Core.Diagnostics;

/// <summary>Result of probing one service the application depends on.</summary>
public sealed record EndpointStatus(
    string Name,
    string Url,
    bool Reachable,
    int? StatusCode,
    TimeSpan Duration,
    string Detail)
{
    public string Label => Reachable
        ? $"erreichbar ({StatusCode}, {Duration.TotalMilliseconds:F0} ms)"
        : Detail;
}

/// <summary>
/// Probes every remote service the application uses and reports which of them
/// answer.
///
/// In a vehicle the usual causes of "nothing works" are a captive portal, a
/// filtering proxy or a dead cellular link — none of which look different from a
/// broken layer name until someone checks. This turns that into one button.
/// </summary>
public sealed class ConnectivityCheck(HttpClient httpClient)
{
    /// <summary>
    /// The endpoints, each with a request cheap enough to run repeatedly.
    /// </summary>
    public static IReadOnlyList<(string Name, string Url)> Endpoints { get; } =
    [
        ("Open-Meteo (Messwerte, Vorhersage)",
            "https://api.open-meteo.com/v1/forecast?latitude=50&longitude=8&current=temperature_2m"),
        ("Bright Sky (DWD-Station, Warnungen, Radar)",
            "https://api.brightsky.dev/current_weather?lat=50&lon=8"),
        ("DWD GeoServer (Karten-Layer)",
            "https://maps.dwd.de/geoserver/dwd/wms?service=WMS&version=1.3.0&request=GetCapabilities"),
        ("RainViewer (Radarbilder)",
            "https://api.rainviewer.com/public/weather-maps.json"),
        ("OpenStreetMap (Kartenkacheln)",
            "https://tile.openstreetmap.org/8/134/86.png"),
        ("basemap.de (amtliche Karte)",
            "https://sgx.geodatenzentrum.de/wmts_basemapde/tile/1.0.0/de_basemapde_web_raster_farbe/default/GLOBAL_WEBMERCATOR/8/86/134.png"),
        ("Nominatim (Adressauflösung)",
            "https://nominatim.openstreetmap.org/reverse?format=jsonv2&lat=50&lon=8")
    ];

    /// <summary>
    /// Probes every endpoint. Runs them one after another rather than in
    /// parallel: several share a host, and hammering it would measure our own
    /// contention instead of the link.
    /// </summary>
    public async Task<IReadOnlyList<EndpointStatus>> RunAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<EndpointStatus>(Endpoints.Count);

        foreach ((string name, string url) in Endpoints)
        {
            results.Add(await ProbeAsync(name, url, cancellationToken).ConfigureAwait(false));
        }

        return results;
    }

    private async Task<EndpointStatus> ProbeAsync(string name, string url, CancellationToken cancellationToken)
    {
        long start = Stopwatch.GetTimestamp();

        try
        {
            // A GET rather than a HEAD: several of these services answer HEAD
            // with 405 even though the real request works.
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using HttpResponseMessage response = await httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            TimeSpan duration = Stopwatch.GetElapsedTime(start);
            bool ok = (int)response.StatusCode is >= 200 and < 400;

            return new EndpointStatus(
                name,
                url,
                ok,
                (int)response.StatusCode,
                duration,
                ok ? "erreichbar" : $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Exception innermost = ex;
            while (innermost.InnerException is not null)
            {
                innermost = innermost.InnerException;
            }

            return new EndpointStatus(
                name,
                url,
                false,
                null,
                Stopwatch.GetElapsedTime(start),
                ex is TaskCanceledException ? "Zeitüberschreitung" : innermost.Message);
        }
    }

    /// <summary>A one-line verdict over the whole run.</summary>
    public static string Summarise(IReadOnlyList<EndpointStatus> results)
    {
        int reachable = results.Count(r => r.Reachable);

        return reachable switch
        {
            0 => "Kein einziger Dienst erreichbar — sehr wahrscheinlich keine Internetverbindung, " +
                 "ein Anmeldeportal im WLAN oder ein blockierender Proxy.",
            _ when reachable == results.Count => $"Alle {results.Count} Dienste erreichbar.",
            _ => $"{reachable} von {results.Count} Diensten erreichbar — die übrigen sind unten benannt. " +
                 "Einzelne Ausfälle deuten auf eine Filterung im Netz oder eine Störung beim Anbieter hin."
        };
    }
}
