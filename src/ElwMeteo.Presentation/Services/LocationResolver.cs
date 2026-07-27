using ElwMeteo.Core.Configuration;
using ElwMeteo.Core.Models;
using ElwMeteo.Core.Services;

namespace ElwMeteo.Presentation.Services;

/// <summary>
/// Decides which position the app should work with, following the configured
/// <see cref="LocationMode"/> and falling back down a chain of decreasing
/// accuracy so there is always *some* usable position.
/// </summary>
public sealed class LocationResolver(
    AppSettings settings,
    GpsSerialService gps,
    IpLocationProvider ipLocation)
{
    /// <summary>A GPS fix older than this is treated as lost.</summary>
    private static readonly TimeSpan MaxFixAge = TimeSpan.FromSeconds(30);

    public async Task<GeoPosition> ResolveAsync(CancellationToken cancellationToken = default)
    {
        switch (settings.LocationMode)
        {
            case LocationMode.Manual:
                return Manual();

            case LocationMode.GpsOnly:
                // Explicit choice: never silently substitute a worse source. Fall
                // back to the station only so the UI has something to render.
                return gps.GetFreshFix(MaxFixAge) ?? Home();

            case LocationMode.Automatic:
            default:
                if (gps.GetFreshFix(MaxFixAge) is { } fix)
                {
                    return fix;
                }

                GeoPosition? ip = await ipLocation.GetAsync(cancellationToken).ConfigureAwait(false);
                if (ip is not null && ip.IsPlausible)
                {
                    return ip;
                }

                return Home();
        }
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
