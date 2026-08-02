using System.Runtime.Versioning;
using ElwMeteo.Core.Models;
using ElwMeteo.Presentation.Platform;
using Windows.Devices.Geolocation;

// Both namespaces call it PositionSource and both are needed here: one is the
// application's own enum, the other says which sensor Windows used.
using WinRtPositionSource = Windows.Devices.Geolocation.PositionSource;

namespace ElwMeteo.App.Platform;

/// <summary>
/// The position Windows itself reports, via the location service.
///
/// Worth having for the machine with no GPS puck on a serial port: a tablet or
/// a vehicle laptop with a built-in GNSS chip already knows where it is, and
/// without this the application would fall straight through to the IP lookup and
/// place the incident in the middle of whichever city the mobile carrier routes
/// through.
///
/// Two things about it are easy to get wrong and are handled deliberately here.
///
/// The permission prompt is shown by <c>RequestAccessAsync</c>, and it has to
/// happen on the interface thread — called from a background thread it throws
/// rather than prompting. So access is requested once, at startup, from the
/// composition root, and the result is remembered.
///
/// And the accuracy is not a detail. The same API answers with five metres from
/// a GNSS chip, forty from WiFi triangulation, and tens of kilometres when
/// Windows quietly fell back to the IP address — the last being no better than
/// what this application can work out for itself, but arriving under a name that
/// sounds authoritative. So the radius is always carried along, shown in the
/// interface, and used by the resolver to reject the useless case.
/// </summary>
[SupportedOSPlatform("windows10.0.17763.0")]
public sealed class WindowsLocationProvider : ISystemLocationProvider
{
    private Geolocator? _geolocator;

    public string Name => "Windows-Ortung";

    public SystemLocationState State { get; private set; } = SystemLocationState.Unknown;

    public string StatusText { get; private set; } = "Noch nicht abgefragt.";

    public async Task<SystemLocationState> RequestAccessAsync()
    {
        try
        {
            GeolocationAccessStatus status = await Geolocator.RequestAccessAsync();

            (State, StatusText) = status switch
            {
                GeolocationAccessStatus.Allowed => (
                    SystemLocationState.Allowed,
                    "Zugriff erlaubt."),

                GeolocationAccessStatus.Denied => (
                    SystemLocationState.Denied,
                    "Zugriff verweigert. In den Windows-Einstellungen unter " +
                    "„Datenschutz und Sicherheit → Standort“ freigeben — dort muss " +
                    "sowohl der Standortdienst selbst als auch der Zugriff für " +
                    "Desktop-Apps eingeschaltet sein."),

                _ => (
                    SystemLocationState.Disabled,
                    "Der Standortdienst ist auf diesem Rechner nicht verfügbar oder abgeschaltet.")
            };
        }
        catch (Exception ex)
        {
            State = SystemLocationState.Unsupported;
            StatusText = $"Standortdienst nicht ansprechbar: {ex.Message}";
        }

        return State;
    }

    public async Task<GeoPosition?> GetAsync(CancellationToken cancellationToken = default)
    {
        if (State is SystemLocationState.Denied or SystemLocationState.Unsupported)
        {
            return null;
        }

        try
        {
            // Created lazily: constructing a Geolocator before access has been
            // granted is what throws on a machine with location switched off.
            _geolocator ??= new Geolocator
            {
                // High accuracy asks for the GNSS chip where there is one. On a
                // machine without one this changes nothing, and it costs no more
                // than the default would.
                DesiredAccuracy = PositionAccuracy.High,

                // A position from the last two minutes is fine. On a vehicle at
                // speed that is already stale, but the GPS receiver is the source
                // for a moving vehicle — this rung is for the parked case.
                ReportInterval = 1000
            };

            // The call blocks until the service has a fix. Without a cap, a
            // machine whose location stack is wedged would hold up the whole
            // weather refresh.
            Geoposition fix = await _geolocator
                .GetGeopositionAsync(TimeSpan.FromMinutes(2), TimeSpan.FromSeconds(10))
                .AsTask(cancellationToken)
                .ConfigureAwait(false);

            Geocoordinate coordinate = fix.Coordinate;

            State = SystemLocationState.Allowed;
            StatusText = $"Position vom {FormatSource(coordinate.PositionSource)} " +
                         $"± {coordinate.Accuracy:F0} m.";

            return new GeoPosition(
                coordinate.Point.Position.Latitude,
                coordinate.Point.Position.Longitude,
                ElwMeteo.Core.Models.PositionSource.SystemService,
                coordinate.Timestamp,
                AltitudeM: coordinate.Point.Position.Altitude,
                AccuracyM: coordinate.Accuracy,
                Description: FormatSource(coordinate.PositionSource));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (UnauthorizedAccessException)
        {
            State = SystemLocationState.Denied;
            StatusText = "Zugriff auf den Standort ist gesperrt.";
            return null;
        }
        catch (Exception ex)
        {
            // Includes the timeout above. Not fatal — the resolver falls through
            // to the IP lookup and then to the stored station.
            StatusText = $"Keine Position erhalten: {ex.Message}";
            return null;
        }
    }

    /// <summary>
    /// Which mechanism produced the fix. This is the difference between a number
    /// worth trusting and one that is a guess with a decimal point, so it goes on
    /// the screen rather than into a log.
    /// </summary>
    private static string FormatSource(WinRtPositionSource source) => source switch
    {
        WinRtPositionSource.Satellite => "GNSS-Empfänger",
        WinRtPositionSource.WiFi => "WLAN-Umfeld",
        WinRtPositionSource.Cellular => "Mobilfunkzelle",
        WinRtPositionSource.IPAddress => "IP-Adresse (grob)",
        WinRtPositionSource.Obfuscated => "absichtlich ungenau",
        _ => "Systemdienst"
    };
}
