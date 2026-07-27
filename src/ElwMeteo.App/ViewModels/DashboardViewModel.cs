using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ElwMeteo.App.Services;
using ElwMeteo.Core.Assessment;
using ElwMeteo.Core.Configuration;
using ElwMeteo.Core.Meteorology;
using ElwMeteo.Core.Models;
using ElwMeteo.Core.Reporting;
using ElwMeteo.Core.Services;

namespace ElwMeteo.App.ViewModels;

/// <summary>A single 15-minute bar in the nowcast strip.</summary>
public sealed record NowcastBar(string TimeLabel, double PrecipitationMm, double BarHeight, bool IsNow)
{
    public string IntensityLabel => PrecipitationMm <= 0 ? "" : $"{PrecipitationMm * 4:F1}";
}

/// <summary>Tab 1 — clock, position and the meteorological picture at that position.</summary>
public sealed partial class DashboardViewModel : ObservableObject
{
    private readonly IWeatherProvider _weather;
    private readonly IWarningProvider _warnings;
    private readonly GeocodingService _geocoding;
    private readonly LocationResolver _location;
    private readonly SnapshotCsvLogger _csvLogger;
    private readonly AppSettings _settings;

    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    public DashboardViewModel(
        IWeatherProvider weather,
        IWarningProvider warnings,
        GeocodingService geocoding,
        LocationResolver location,
        SnapshotCsvLogger csvLogger,
        AppSettings settings)
    {
        _weather = weather;
        _warnings = warnings;
        _geocoding = geocoding;
        _location = location;
        _csvLogger = csvLogger;
        _settings = settings;
    }

    // ------------------------------------------------------------- state

    /// <summary>Raised whenever a fresh assessment is available, so the map can follow.</summary>
    public event Action<TacticalAssessment>? AssessmentUpdated;

    [ObservableProperty]
    private TacticalAssessment? _assessment;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = "Noch keine Daten abgerufen.";

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private string _lastUpdateLabel = "—";

    [ObservableProperty]
    private string _dataAgeLabel = string.Empty;

    /// <summary>True once the data is old enough that it should not be trusted blindly.</summary>
    [ObservableProperty]
    private bool _isStale;

    // ---------------------------------------------------------- position

    [ObservableProperty]
    private string _positionDecimal = "—";

    [ObservableProperty]
    private string _positionNautical = "—";

    [ObservableProperty]
    private string _positionSource = "—";

    [ObservableProperty]
    private string _addressLine = string.Empty;

    // ----------------------------------------------------------- weather

    [ObservableProperty]
    private string _temperature = "—";

    [ObservableProperty]
    private string _apparentTemperature = "—";

    [ObservableProperty]
    private string _conditionText = "—";

    [ObservableProperty]
    private string _conditionGlyph = "•";

    [ObservableProperty]
    private string _humidity = "—";

    [ObservableProperty]
    private string _dewPoint = "—";

    [ObservableProperty]
    private string _pressure = "—";

    [ObservableProperty]
    private string _windSummary = "—";

    [ObservableProperty]
    private string _windDetail = "—";

    [ObservableProperty]
    private string _gustSummary = "—";

    /// <summary>Rotation for the wind arrow: points the way the air is going.</summary>
    [ObservableProperty]
    private double _windArrowAngle;

    [ObservableProperty]
    private string _cloudCover = "—";

    [ObservableProperty]
    private string _visibility = "—";

    [ObservableProperty]
    private string _precipitation = "—";

    [ObservableProperty]
    private string _cape = "—";

    // ------------------------------------------------------------ derived

    [ObservableProperty]
    private string _stabilityClass = "—";

    [ObservableProperty]
    private string _stabilityLabel = "—";

    [ObservableProperty]
    private string _stabilityNote = string.Empty;

    [ObservableProperty]
    private string _downwind = "—";

    [ObservableProperty]
    private string _hazardRange = "—";

    [ObservableProperty]
    private string _wbgt = "—";

    [ObservableProperty]
    private string _wetBulb = "—";

    [ObservableProperty]
    private string _absoluteHumidity = "—";

    [ObservableProperty]
    private string _cloudBase = "—";

    [ObservableProperty]
    private string _fireRisk = "—";

    [ObservableProperty]
    private string _fireRiskNote = string.Empty;

    // ------------------------------------------------------------ sun/moon

    [ObservableProperty]
    private string _sunrise = "—";

    [ObservableProperty]
    private string _sunset = "—";

    [ObservableProperty]
    private string _civilTwilight = "—";

    [ObservableProperty]
    private string _dayLength = "—";

    [ObservableProperty]
    private string _solarPosition = "—";

    [ObservableProperty]
    private string _moon = "—";

    // ------------------------------------------------------------ nowcast

    [ObservableProperty]
    private string _nowcastSummary = "—";

    public ObservableCollection<NowcastBar> NowcastBars { get; } = [];

    public ObservableCollection<TacticalHint> Hints { get; } = [];

    public ObservableCollection<DwdWarning> Warnings { get; } = [];

    [ObservableProperty]
    private string _warningSummary = "Keine Warnungen für diese Position.";

    [ObservableProperty]
    private bool _hasWarnings;

    // ------------------------------------------------------------ commands

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        HasError = false;
        StatusMessage = "Daten werden abgerufen …";

        try
        {
            GeoPosition position = await _location.ResolveAsync(cancellationToken).ConfigureAwait(true);
            WeatherSnapshot snapshot = await _weather.GetAsync(position, cancellationToken).ConfigureAwait(true);

            DateTimeOffset now = DateTimeOffset.Now;
            TacticalAssessment assessment = WeatherAssessor.Assess(snapshot, now);

            Assessment = assessment;
            ApplyAssessment(assessment, now);

            LastUpdateLabel = now.ToString("HH:mm:ss");
            StatusMessage = $"Aktualisiert {LastUpdateLabel} · Quelle {snapshot.ModelName}";

            AssessmentUpdated?.Invoke(assessment);

            // The remaining steps are decoration: a failure there must not mark
            // the refresh as failed, because the weather itself arrived fine.
            await UpdateAddressAsync(position, cancellationToken).ConfigureAwait(true);
            await UpdateWarningsAsync(position, cancellationToken).ConfigureAwait(true);
            await LogSnapshotAsync(assessment, now, cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StatusMessage = "Abruf abgebrochen.";
        }
        catch (TaskCanceledException)
        {
            // Not our token: this is HttpClient's own timeout expiring.
            HasError = true;
            StatusMessage = "Zeitüberschreitung beim Abruf — Netzverbindung prüfen.";
        }
        catch (Exception ex)
        {
            HasError = true;
            StatusMessage = ex is WeatherProviderException
                ? ex.Message
                : $"Abruf fehlgeschlagen: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void CopyLogEntry()
    {
        if (Assessment is null)
        {
            return;
        }

        string text = WeatherReportFormatter.BuildLogEntry(Assessment, DateTimeOffset.Now, AddressLine);
        if (TrySetClipboard(text))
        {
            StatusMessage = "Wettermeldung in die Zwischenablage kopiert.";
        }
    }

    [RelayCommand]
    private void CopyRadioLine()
    {
        if (Assessment is null)
        {
            return;
        }

        if (TrySetClipboard(WeatherReportFormatter.BuildRadioLine(Assessment)))
        {
            StatusMessage = "Funkspruch in die Zwischenablage kopiert.";
        }
    }

    [RelayCommand]
    private void CopyPosition()
    {
        if (Assessment is null)
        {
            return;
        }

        LatLon p = Assessment.Snapshot.Position.ToLatLon();
        string text = $"{p.Latitude.ToString("F5", CultureInfo.InvariantCulture)}, " +
                      $"{p.Longitude.ToString("F5", CultureInfo.InvariantCulture)}\n" +
                      Geodesy.FormatDegreesDecimalMinutes(p);

        if (TrySetClipboard(text))
        {
            StatusMessage = "Position in die Zwischenablage kopiert.";
        }
    }

    /// <summary>
    /// The Windows clipboard is shared and can be locked by another process;
    /// a failure here is an inconvenience, not an error worth a dialog.
    /// </summary>
    private bool TrySetClipboard(string text)
    {
        try
        {
            Clipboard.SetText(text);
            return true;
        }
        catch (Exception)
        {
            StatusMessage = "Zwischenablage ist belegt — bitte erneut versuchen.";
            return false;
        }
    }

    // ------------------------------------------------------------ helpers

    /// <summary>Called on every clock tick to keep the "age" indicator honest.</summary>
    public void UpdateAge(DateTimeOffset now)
    {
        if (Assessment is null)
        {
            return;
        }

        TimeSpan age = Assessment.Snapshot.AgeAt(now.ToUniversalTime());
        IsStale = age > TimeSpan.FromMinutes(20);

        DataAgeLabel = age.TotalMinutes switch
        {
            < 1 => "gerade eben",
            < 60 => $"vor {(int)age.TotalMinutes} min",
            _ => $"vor {(int)age.TotalHours} h {age.Minutes} min"
        };
    }

    private async Task UpdateAddressAsync(GeoPosition position, CancellationToken cancellationToken)
    {
        string? address = await _geocoding.ReverseAsync(position, cancellationToken).ConfigureAwait(true);
        AddressLine = address ?? string.Empty;
    }

    private async Task UpdateWarningsAsync(GeoPosition position, CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<DwdWarning> warnings = await _warnings.GetAsync(position, cancellationToken)
                .ConfigureAwait(true);

            Warnings.Clear();
            foreach (DwdWarning warning in warnings)
            {
                Warnings.Add(warning);
            }

            HasWarnings = Warnings.Count > 0;
            WarningSummary = HasWarnings
                ? $"{Warnings.Count} amtliche Warnung(en) — höchste Stufe: {Warnings[0].LevelLabel}"
                : "Keine amtlichen Warnungen für diese Position.";
        }
        catch (WarningProviderException ex)
        {
            Warnings.Clear();
            HasWarnings = false;
            WarningSummary = ex.Message;
        }
    }

    private async Task LogSnapshotAsync(TacticalAssessment assessment, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!_settings.CsvLoggingEnabled)
        {
            return;
        }

        try
        {
            await _csvLogger.AppendAsync(assessment, now, cancellationToken).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusMessage = $"Protokoll konnte nicht geschrieben werden: {ex.Message}";
        }
    }

    private void ApplyAssessment(TacticalAssessment a, DateTimeOffset now)
    {
        WeatherSnapshot s = a.Snapshot;

        // -- position
        LatLon p = s.Position.ToLatLon();
        PositionDecimal = $"{p.Latitude.ToString("F5", German)}  /  {p.Longitude.ToString("F5", German)}";
        PositionNautical = Geodesy.FormatDegreesDecimalMinutes(p);
        PositionSource = s.Position.SourceLabel;

        // -- current conditions
        Temperature = Unit(s.TemperatureC, "F1", "°C");
        ApparentTemperature = s.ApparentTemperatureC is null ? "—" : $"gefühlt {Unit(s.ApparentTemperatureC, "F1", "°C")}";
        ConditionText = WeatherCodes.Describe(s.WeatherCode);
        ConditionGlyph = WeatherCodes.Glyph(s.WeatherCode, s.IsDay);
        Humidity = Unit(s.RelativeHumidityPercent, "F0", "%");
        DewPoint = Unit(s.DewPointC, "F1", "°C");
        Pressure = Unit(s.PressureMslHpa, "F0", "hPa");
        CloudCover = Unit(s.CloudCoverPercent, "F0", "%");
        Visibility = FormatVisibility(s.VisibilityM);
        Precipitation = Unit(s.PrecipitationMm, "F1", "mm/h");
        Cape = Unit(s.CapeJkg, "F0", "J/kg");

        // -- wind
        if (s.WindSpeedMs is { } wind)
        {
            WindSummary = $"{WindScale.MsToKmh(wind).ToString("F0", German)} km/h";
            WindDetail = $"aus {a.WindFromCompass} ({(s.WindDirectionDeg ?? 0).ToString("F0", German)}°) · " +
                         $"{wind.ToString("F1", German)} m/s · {a.Beaufort.Force} Bft {a.Beaufort.Description}";
        }
        else
        {
            WindSummary = "—";
            WindDetail = "keine Winddaten";
        }

        GustSummary = s.WindGustMs is { } gust
            ? $"Böen {WindScale.MsToKmh(gust).ToString("F0", German)} km/h"
            : "Böen —";

        // The arrow points downwind, i.e. the way a plume would drift.
        WindArrowAngle = a.DownwindBearingDeg;

        // -- dispersion
        StabilityClass = $"{a.Stability.KlugManier}  ·  Pasquill {a.Stability.Pasquill}";
        StabilityLabel = a.Stability.Label;
        StabilityNote = a.Stability.TacticalNote;
        Downwind = $"nach {a.DownwindCompass} ({a.DownwindBearingDeg.ToString("F0", German)}°)";

        double range = _settings.HazardRangeMetresOverride ?? HazardPlume.SuggestedRangeFor(a.Stability.Pasquill);
        TimeSpan? travel = HazardPlume.TravelTime(range, s.WindSpeedMs ?? 0);
        HazardRange = travel is null
            ? $"{range:F0} m Richtwert · windstill, keine Transportzeit"
            : $"{range:F0} m Richtwert · Fahne dort nach ca. {travel.Value.TotalMinutes:F0} min";

        // -- load on crews
        Wbgt = Unit(a.WbgtShadeC, "F1", "°C");
        WetBulb = Unit(a.WetBulbC, "F1", "°C");
        AbsoluteHumidity = Unit(a.AbsoluteHumidityGm3, "F1", "g/m³");
        CloudBase = a.ConvectiveCloudBaseM is { } baseHeight ? $"{baseHeight.ToString("F0", German)} m ü. Grund" : "—";
        FireRisk = $"{a.FireRisk.Label} (Index {a.FireRisk.AngstromIndex.ToString("F1", German)})";
        FireRiskNote = a.FireRisk.Note;

        // -- daylight
        Sunrise = Clock(a.SolarDay.Sunrise);
        Sunset = Clock(a.SolarDay.Sunset);
        CivilTwilight = $"{Clock(a.SolarDay.CivilDawn)} – {Clock(a.SolarDay.CivilDusk)}";
        DayLength = a.SolarDay.DayLength is { } length
            ? $"{(int)length.TotalHours} h {length.Minutes} min"
            : a.SolarDay.MidnightSun ? "Mitternachtssonne" : "Polarnacht";
        SolarPosition = $"{a.SolarPosition.ElevationDeg.ToString("F0", German)}° Höhe · " +
                        $"{a.SolarPosition.AzimuthDeg.ToString("F0", German)}° Azimut";
        Moon = $"{a.Moon.Name} · {a.Moon.IlluminatedPercent} % beleuchtet";

        // -- nowcast
        NowcastSummary = a.Outlook.Summary;
        BuildNowcastBars(a, now);

        // -- hints
        Hints.Clear();
        foreach (TacticalHint hint in a.Hints)
        {
            Hints.Add(hint);
        }

        UpdateAge(now);
    }

    private void BuildNowcastBars(TacticalAssessment a, DateTimeOffset now)
    {
        NowcastBars.Clear();

        var steps = a.Snapshot.Nowcast
            .Where(step => step.Time >= now.AddMinutes(-30))
            .OrderBy(step => step.Time)
            .Take(12)
            .ToList();

        if (steps.Count == 0)
        {
            return;
        }

        // Scale to the busiest bar, with a floor so light drizzle stays visible.
        double peak = Math.Max(0.5, steps.Max(step => step.PrecipitationMm ?? 0.0));
        const double maxHeight = 54.0;

        foreach (NowcastStep step in steps)
        {
            double value = step.PrecipitationMm ?? 0.0;
            NowcastBars.Add(new NowcastBar(
                step.Time.ToLocalTime().ToString("HH:mm"),
                value,
                Math.Max(2.0, value / peak * maxHeight),
                step.Time <= now && step.Time.AddMinutes(15) > now));
        }
    }

    private static string Unit(double? value, string format, string unit) =>
        value is null || double.IsNaN(value.Value) ? "—" : $"{value.Value.ToString(format, German)} {unit}";

    private static string FormatVisibility(double? metres) => metres switch
    {
        null => "—",
        >= 10_000 => "> 10 km",
        >= 1_000 => $"{(metres.Value / 1000.0).ToString("F1", German)} km",
        _ => $"{metres.Value.ToString("F0", German)} m"
    };

    private static string Clock(DateTimeOffset? instant) => instant?.ToLocalTime().ToString("HH:mm") ?? "—";
}
