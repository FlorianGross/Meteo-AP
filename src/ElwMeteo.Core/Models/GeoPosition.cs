using ElwMeteo.Core.Meteorology;

namespace ElwMeteo.Core.Models;

public enum PositionSource
{
    /// <summary>Typed in or picked on the map by the operator.</summary>
    Manual,
    /// <summary>Derived from the public IP address — city-level accuracy at best.</summary>
    IpLookup,
    /// <summary>NMEA sentence from a GPS receiver on a serial port.</summary>
    Gps,
    /// <summary>Stored standing location, e.g. the fire station.</summary>
    Preset,
    /// <summary>
    /// The operating system's own location service. Accuracy varies by an order
    /// of magnitude depending on what the machine actually has — a built-in GNSS
    /// receiver gives metres, WiFi triangulation gives tens of metres, and with
    /// neither it falls back to the IP address and is no better than a guess.
    /// Which is why the accuracy is always carried alongside.
    /// </summary>
    SystemService
}

public sealed record GeoPosition(
    double Latitude,
    double Longitude,
    PositionSource Source,
    DateTimeOffset TimestampUtc,
    double? AltitudeM = null,
    double? AccuracyM = null,
    string? Description = null)
{
    public LatLon ToLatLon() => new(Latitude, Longitude);

    /// <summary>Short label for the status bar, e.g. "GPS ±4 m".</summary>
    public string SourceLabel => Source switch
    {
        PositionSource.Gps => AccuracyM is not null ? $"GPS ±{AccuracyM.Value:F0} m" : "GPS",
        PositionSource.IpLookup => "IP-Ortung (ungenau)",
        PositionSource.Preset => "Voreinstellung",
        // The radius is not decoration here: the same label can mean five metres
        // or five kilometres, and only the number says which.
        PositionSource.SystemService => AccuracyM is not null
            ? $"Windows-Ortung ±{AccuracyM.Value:F0} m"
            : "Windows-Ortung (Genauigkeit unbekannt)",
        _ => "manuell"
    };

    public bool IsPlausible =>
        Latitude is >= -90 and <= 90 &&
        Longitude is >= -180 and <= 180 &&
        !(Latitude == 0 && Longitude == 0);
}
