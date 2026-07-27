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
    Preset
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
        _ => "manuell"
    };

    public bool IsPlausible =>
        Latitude is >= -90 and <= 90 &&
        Longitude is >= -180 and <= 180 &&
        !(Latitude == 0 && Longitude == 0);
}
