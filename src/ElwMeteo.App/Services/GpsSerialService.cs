using System.IO;
using System.IO.Ports;
using ElwMeteo.Core.Models;
using ElwMeteo.Core.Services;

namespace ElwMeteo.App.Services;

/// <summary>
/// Reads NMEA sentences from a serial GPS receiver. Many command vehicles have
/// one wired in already, and it is by far the most accurate position source
/// available offline.
///
/// The service is intentionally forgiving: a missing or busy port is reported via
/// <see cref="StatusChanged"/> and never throws into the UI, because losing GPS
/// must degrade the app to manual positioning rather than break it.
/// </summary>
public sealed class GpsSerialService : IDisposable
{
    private readonly object _sync = new();
    private SerialPort? _port;
    private GeoPosition? _lastFix;

    /// <summary>Raised on the reader thread whenever a valid fix arrives.</summary>
    public event Action<GeoPosition>? FixReceived;

    /// <summary>Raised with a human-readable status for the status bar.</summary>
    public event Action<string>? StatusChanged;

    public bool IsOpen
    {
        get
        {
            lock (_sync)
            {
                return _port?.IsOpen == true;
            }
        }
    }

    public string? PortName
    {
        get
        {
            lock (_sync)
            {
                return _port?.PortName;
            }
        }
    }

    /// <summary>The most recent fix, or null if none has arrived yet.</summary>
    public GeoPosition? LastFix
    {
        get
        {
            lock (_sync)
            {
                return _lastFix;
            }
        }
    }

    /// <summary>Most recent fix, but only if it is younger than <paramref name="maxAge"/>.</summary>
    public GeoPosition? GetFreshFix(TimeSpan maxAge)
    {
        GeoPosition? fix = LastFix;
        if (fix is null)
        {
            return null;
        }

        return DateTimeOffset.UtcNow - fix.TimestampUtc <= maxAge ? fix : null;
    }

    public static IReadOnlyList<string> AvailablePorts()
    {
        try
        {
            return SerialPort.GetPortNames().OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (Exception)
        {
            // Enumerating ports can fail on locked-down machines; an empty list is fine.
            return [];
        }
    }

    /// <summary>Opens the port. Returns false and reports a status on failure.</summary>
    public bool Open(string portName, int baudRate)
    {
        Close();

        if (string.IsNullOrWhiteSpace(portName))
        {
            StatusChanged?.Invoke("GPS: kein Port konfiguriert.");
            return false;
        }

        try
        {
            var port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
            {
                NewLine = "\r\n",
                ReadTimeout = 2000,
                DtrEnable = true,
                RtsEnable = true
            };

            port.DataReceived += OnDataReceived;
            port.ErrorReceived += OnErrorReceived;
            port.Open();

            lock (_sync)
            {
                _port = port;
            }

            StatusChanged?.Invoke($"GPS: {portName} geöffnet ({baudRate} Baud), warte auf Fix.");
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or ArgumentException or InvalidOperationException)
        {
            StatusChanged?.Invoke($"GPS: {portName} konnte nicht geöffnet werden — {ex.Message}");
            return false;
        }
    }

    public void Close()
    {
        SerialPort? port;
        lock (_sync)
        {
            port = _port;
            _port = null;
        }

        if (port is null)
        {
            return;
        }

        try
        {
            port.DataReceived -= OnDataReceived;
            port.ErrorReceived -= OnErrorReceived;

            if (port.IsOpen)
            {
                port.Close();
            }
        }
        catch (Exception)
        {
            // Closing a port that the OS already tore down is not worth reporting.
        }
        finally
        {
            port.Dispose();
        }
    }

    private void OnDataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        SerialPort? port;
        lock (_sync)
        {
            port = _port;
        }

        if (port is null || !port.IsOpen)
        {
            return;
        }

        try
        {
            // Drain whatever complete lines are buffered.
            while (port.IsOpen && port.BytesToRead > 0)
            {
                string line = port.ReadLine();
                NmeaFix? fix = NmeaParser.ParseSentence(line);
                if (fix is null)
                {
                    continue;
                }

                var position = new GeoPosition(
                    fix.Latitude,
                    fix.Longitude,
                    PositionSource.Gps,
                    DateTimeOffset.UtcNow,
                    fix.AltitudeM,
                    fix.EstimatedAccuracyM,
                    fix.SatelliteCount is { } satellites ? $"{satellites} Satelliten" : null);

                if (!position.IsPlausible)
                {
                    continue;
                }

                lock (_sync)
                {
                    _lastFix = position;
                }

                FixReceived?.Invoke(position);
            }
        }
        catch (TimeoutException)
        {
            // A partial line at the end of the buffer; the next event picks it up.
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            StatusChanged?.Invoke($"GPS: Lesefehler — {ex.Message}");
        }
    }

    private void OnErrorReceived(object sender, SerialErrorReceivedEventArgs e) =>
        StatusChanged?.Invoke($"GPS: Schnittstellenfehler ({e.EventType}).");

    public void Dispose() => Close();
}
