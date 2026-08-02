using ElwMeteo.Core.Configuration;
using ElwMeteo.Core.Models;
using ElwMeteo.Core.Services;
using ElwMeteo.Presentation.Platform;

namespace ElwMeteo.Presentation.Services;

/// <summary>
/// Decides which position the app should work with, following the configured
/// <see cref="LocationMode"/> and falling back down a chain of decreasing
/// accuracy so there is always *some* usable position.
/// </summary>
public sealed class LocationResolver(
    AppSettings settings,
    GpsSerialService gps,
    IpLocationProvider ipLocation,
    ISystemLocationProvider? systemLocation = null)
{
    /// <summary>A GPS fix older than this is treated as lost.</summary>
    private static readonly TimeSpan MaxFixAge = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Past this radius the system service is no longer telling us where the
    /// vehicle is, it is telling us which city it is near — which is what the IP
    /// lookup already does, and that one at least says so in its name. Fifty
    /// kilometres is generous on purpose: the point is to catch the case where
    /// Windows quietly fell back to the IP address itself, not to be strict
    /// about a mediocre WiFi fix.
    /// </summary>
    private const double MaxUsefulAccuracyMetres = 50_000;

    /// <summary>Why the last resolve chose what it chose, for the diagnostics tab.</summary>
    public string LastDecision { get; private set; } = "noch nichts abgerufen";

    public async Task<GeoPosition> ResolveAsync(CancellationToken cancellationToken = default)
    {
        switch (settings.LocationMode)
        {
            case LocationMode.Manual:
                return Decide(Manual(), "manuelle Koordinaten eingestellt");

            case LocationMode.GpsOnly:
                // Explicit choice: never silently substitute a worse source. Fall
                // back to the station only so the UI has something to render.
                return gps.GetFreshFix(MaxFixAge) is { } only
                    ? Decide(only, "GPS-Empfänger")
                    : Decide(Home(), "kein GPS-Fix — Standort als Rückfall");

            case LocationMode.Automatic:
            default:
                if (gps.GetFreshFix(MaxFixAge) is { } fix)
                {
                    return Decide(fix, "GPS-Empfänger");
                }

                // Ahead of the IP lookup because it is usually far better, and
                // behind the receiver because it is never better than that.
                if (await SystemAsync(cancellationToken).ConfigureAwait(false) is { } system)
                {
                    return Decide(system, $"{systemLocation?.Name ?? "Systemortung"} ±{system.AccuracyM:F0} m");
                }

                GeoPosition? ip = await ipLocation.GetAsync(cancellationToken).ConfigureAwait(false);
                if (ip is not null && ip.IsPlausible)
                {
                    return Decide(ip, "IP-Ortung");
                }

                return Decide(Home(), "keine Ortung möglich — Standort als Rückfall");
        }
    }

    /// <summary>
    /// The system position, or null when it is switched off, refused, absent or
    /// so coarse that it says nothing the IP lookup would not.
    /// </summary>
    private async Task<GeoPosition?> SystemAsync(CancellationToken cancellationToken)
    {
        if (systemLocation is null || !settings.UseSystemLocation)
        {
            return null;
        }

        GeoPosition? position;

        try
        {
            position = await systemLocation.GetAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A location service that misbehaves must not take the weather
            // refresh with it; the chain below still has two more rungs. A
            // cancellation is the caller's own doing and belongs upwards.
            return null;
        }

        if (position is null || !position.IsPlausible)
        {
            return null;
        }

        return position.AccuracyM is > MaxUsefulAccuracyMetres ? null : position;
    }

    private GeoPosition Decide(GeoPosition position, string reason)
    {
        LastDecision = reason;
        return position;
    }

    private GeoPosition Manual() => new(
        settings.ManualLatitude,
        settings.ManualLongitude,
        PositionSource.Manual,
        DateTimeOffset.UtcNow);

    private GeoPosition Home() => new(
        settings.HomeLatitude,
        settings.HomeLongitude,
        PositionSource.Preset,
        DateTimeOffset.UtcNow,
        Description: settings.HomeName);
}
