using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ElwMeteo.Presentation.Services;
using ElwMeteo.Core.Assessment;
using ElwMeteo.Presentation.Platform;
using ElwMeteo.Core.Configuration;
using ElwMeteo.Core.Meteorology;
using ElwMeteo.Core.Models;
using ElwMeteo.Core.Persistence;
using ElwMeteo.Core.Reporting;
using ElwMeteo.Core.Services;

namespace ElwMeteo.Presentation.ViewModels;

/// <summary>A single 15-minute bar in the nowcast strip.</summary>
public sealed record NowcastBar(string TimeLabel, double PrecipitationMm, double BarHeight, bool IsNow)
{
    public string IntensityLabel => PrecipitationMm <= 0 ? "" : $"{PrecipitationMm * 4:F1}";
}

/// <summary>One hour of the wind forecast strip: speed bar, gust cap and an arrow.</summary>
public sealed record WindForecastBar(
    string TimeLabel,
    double SpeedKmh,
    double GustKmh,
    double SpeedBarHeight,
    double GustBarHeight,
    double ArrowAngle,
    string Compass,
    bool IsShiftPoint)
{
    public string SpeedLabel => $"{SpeedKmh:F0}";

    public string GustLabel => GustKmh >= SpeedKmh + 5 ? $"{GustKmh:F0}" : string.Empty;
}

/// <summary>Tab 1 — clock, position and the meteorological picture at that position.</summary>
public sealed partial class DashboardViewModel : ObservableObject
{
    private readonly IWeatherProvider _weather;
    private readonly IWarningProvider _warnings;
    private readonly GeocodingService _geocoding;
    private readonly LocationResolver _location;
    private readonly SnapshotCsvLogger _csvLogger;
    private readonly BrightSkyProvider _brightSky;
    private readonly AppSettings _settings;
    private readonly IClipboardService _clipboard;
    private readonly SnapshotCache _cache;
    private readonly ReportPrinter _reports;
    private readonly WarningMonitor _monitor = new();

    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    public DashboardViewModel(
        IWeatherProvider weather,
        IWarningProvider warnings,
        GeocodingService geocoding,
        LocationResolver location,
        SnapshotCsvLogger csvLogger,
        BrightSkyProvider brightSky,
        AppSettings settings,
        IClipboardService clipboard,
        SnapshotCache cache,
        ReportPrinter reports)
    {
        _weather = weather;
        _warnings = warnings;
        _geocoding = geocoding;
        _location = location;
        _csvLogger = csvLogger;
        _brightSky = brightSky;
        _settings = settings;
        _clipboard = clipboard;
        _cache = cache;
        _reports = reports;
        _monitor.Threshold = settings.AlertMinimumLevel;
    }

    // ------------------------------------------------------------- state

    /// <summary>Raised whenever a fresh assessment is available, so the map can follow.</summary>
    public event Action<TacticalAssessment>? AssessmentUpdated;

    /// <summary>Raised when warnings arrive that nobody has seen yet.</summary>
    public event Action<IReadOnlyList<WarningAlert>>? WarningsAlerted;

    /// <summary>True while the shown data comes from the cache rather than the network.</summary>
    [ObservableProperty]
    private bool _isOffline;

    [ObservableProperty]
    private string _offlineNotice = string.Empty;

    /// <summary>Countdown text while a failed fetch is being retried.</summary>
    [ObservableProperty]
    private string _retryNotice = string.Empty;

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

    // -------------------------------------------------------- wind outlook

    [ObservableProperty]
    private string _windShiftHeadline = "—";

    [ObservableProperty]
    private string _windShiftDetail = string.Empty;

    [ObservableProperty]
    private string _gustPeakLabel = string.Empty;

    /// <summary>Colours the header entry: red for a big turn, amber for a small one.</summary>
    [ObservableProperty]
    private UiColour _windShiftColour = UiColour.Grey;

    public ObservableCollection<WindForecastBar> WindForecast { get; } = [];

    public ObservableCollection<NowcastBar> NowcastBars { get; } = [];

    public ObservableCollection<TacticalHint> Hints { get; } = [];

    public ObservableCollection<DwdWarning> Warnings { get; } = [];

    // --------------------------------------------- measured station values

    /// <summary>True once a DWD station reading has been retrieved.</summary>
    [ObservableProperty]
    private bool _hasStation;

    [ObservableProperty]
    private string _stationName = "—";

    [ObservableProperty]
    private string _stationDistance = string.Empty;

    [ObservableProperty]
    private string _stationAge = string.Empty;

    [ObservableProperty]
    private string _stationTemperature = "—";

    [ObservableProperty]
    private string _stationWind = "—";

    [ObservableProperty]
    private string _stationHumidity = "—";

    [ObservableProperty]
    private string _stationPressure = "—";

    /// <summary>Set when the nearest station is too far away to speak for the site.</summary>
    [ObservableProperty]
    private string _stationCaveat = string.Empty;

    /// <summary>Difference between the model temperature and the measured one.</summary>
    [ObservableProperty]
    private string _modelDeviation = string.Empty;

    // ------------------------------------------------- DWD radar at the point

    [ObservableProperty]
    private string _dwdRadarSummary = "DWD-Radarwerte noch nicht abgerufen.";

    [ObservableProperty]
    private bool _hasDwdRadar;

    public ObservableCollection<NowcastBar> DwdRadarBars { get; } = [];

    [ObservableProperty]
    private string _warningSource = string.Empty;

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

            IsOffline = false;
            OfflineNotice = string.Empty;
            RetryNotice = string.Empty;

            LastUpdateLabel = now.ToString("HH:mm:ss");
            StatusMessage = $"Aktualisiert {LastUpdateLabel} · Quelle {snapshot.ModelName}";

            AssessmentUpdated?.Invoke(assessment);

            // The remaining steps are decoration: a failure there must not mark
            // the refresh as failed, because the weather itself arrived fine.
            await UpdateAddressAsync(position, cancellationToken).ConfigureAwait(true);
            await UpdateStationAsync(position, snapshot, cancellationToken).ConfigureAwait(true);
            await UpdateDwdRadarAsync(position, now, cancellationToken).ConfigureAwait(true);
            await UpdateWarningsAsync(position, cancellationToken).ConfigureAwait(true);
            await LogSnapshotAsync(assessment, now, cancellationToken).ConfigureAwait(true);

            // Written last, so only a picture that came through completely is
            // the one offered on the next start without a network.
            StoreState(snapshot, now);
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

        if (HasError && Assessment is null)
        {
            RestoreFromCache();
        }
    }

    // --------------------------------------------------- stored last state

    /// <summary>
    /// Puts the last good picture on screen when the network has never answered
    /// in this session.
    ///
    /// Only ever when there is nothing else: a stored reading must never
    /// overwrite one that arrived a minute ago just because a later refresh
    /// failed. The banner is worded so nobody mistakes it for current — the
    /// value of this is a starting point for the first assessment, not a
    /// substitute for the real thing.
    /// </summary>
    private void RestoreFromCache()
    {
        CachedState? state = _cache.Load(DateTimeOffset.Now);

        if (state is null)
        {
            return;
        }

        DateTimeOffset now = DateTimeOffset.Now;
        TacticalAssessment assessment = WeatherAssessor.Assess(state.Snapshot, now);

        Assessment = assessment;
        ApplyAssessment(assessment, now);

        Warnings.Clear();
        foreach (DwdWarning warning in state.Warnings)
        {
            Warnings.Add(warning);
        }

        HasWarnings = Warnings.Count > 0;
        AddressLine = state.AddressLine ?? string.Empty;
        WarningSource = state.WarningSource ?? string.Empty;

        IsOffline = true;
        OfflineNotice =
            $"Gespeicherter Stand von {state.SavedAtUtc.ToLocalTime():HH:mm} Uhr " +
            $"({SnapshotCache.DescribeAge(state.AgeAt(now))}) — kein Abruf möglich. " +
            "Werte beschreiben nicht die aktuelle Lage.";

        LastUpdateLabel = state.SavedAtUtc.ToLocalTime().ToString("HH:mm:ss");

        // The map and the trend chart get it too: half the application showing
        // the stored picture and half of it showing nothing would be worse than
        // either.
        AssessmentUpdated?.Invoke(assessment);
    }

    private void StoreState(WeatherSnapshot snapshot, DateTimeOffset now)
    {
        _cache.Save(new CachedState
        {
            Snapshot = snapshot,
            Warnings = Warnings.ToList(),
            AddressLine = string.IsNullOrWhiteSpace(AddressLine) ? null : AddressLine,
            WarningSource = string.IsNullOrWhiteSpace(WarningSource) ? null : WarningSource,
            SavedAtUtc = now.ToUniversalTime()
        });
    }

    /// <summary>Picks up an alert threshold changed on the settings tab.</summary>
    public void ApplyAlertSettings() => _monitor.Threshold = _settings.AlertMinimumLevel;

    /// <summary>Loads the stored state at start, before the first fetch answers.</summary>
    public void ShowStoredStateIfAny()
    {
        if (Assessment is null)
        {
            RestoreFromCache();
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

    /// <summary>Free text naming the operation; goes into the report header.</summary>
    [ObservableProperty]
    private string _incidentLabel = string.Empty;

    /// <summary>
    /// Writes the printable report and opens it. Everything on screen goes in,
    /// including the warnings and the offline banner if the picture is a stored
    /// one — a printed sheet outlives the session that produced it, and one that
    /// does not say how old its numbers are is a trap.
    /// </summary>
    [RelayCommand]
    private void PrintReport()
    {
        if (Assessment is null)
        {
            StatusMessage = "Noch keine Daten — es gibt nichts zu drucken.";
            return;
        }

        DateTimeOffset now = DateTimeOffset.Now;

        var options = new ReportOptions
        {
            IncidentLabel = string.IsNullOrWhiteSpace(IncidentLabel) ? null : IncidentLabel.Trim(),
            AddressLine = string.IsNullOrWhiteSpace(AddressLine) ? null : AddressLine,
            Warnings = Warnings.ToList(),
            WarningSource = string.IsNullOrWhiteSpace(WarningSource) ? null : WarningSource,
            OfflineAge = IsOffline ? Assessment.Snapshot.AgeAt(now) : null,
            Organisation = string.IsNullOrWhiteSpace(_settings.HomeName) ? null : _settings.HomeName
        };

        string html = WeatherReportPage.Build(Assessment, now, options);

        _reports.Produce(html, now, out string message);
        StatusMessage = message;
    }

    [RelayCommand]
    private void OpenReportDirectory()
    {
        if (!_reports.OpenDirectory(out string? error))
        {
            StatusMessage = $"Berichtsordner ließ sich nicht öffnen: {error}";
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
    /// The clipboard is shared and can be locked by another process; a failure
    /// here is an inconvenience, not an error worth a dialog.
    /// </summary>
    private bool TrySetClipboard(string text)
    {
        if (_clipboard.TrySetText(text))
        {
            return true;
        }

        StatusMessage = "Zwischenablage ist belegt — bitte erneut versuchen.";
        return false;
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

    /// <summary>
    /// Pulls the nearest DWD station reading. Model output is interpolated; this
    /// is what an instrument actually recorded, which is the number that settles
    /// an argument on scene.
    /// </summary>
    private async Task UpdateStationAsync(
        GeoPosition position,
        WeatherSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        StationObservation? station = await _brightSky
            .GetCurrentStationAsync(position, cancellationToken)
            .ConfigureAwait(true);

        if (station is null)
        {
            HasStation = false;
            return;
        }

        HasStation = true;
        StationName = station.StationName;
        StationDistance = $"{station.DistanceLabel} entfernt";

        TimeSpan age = station.AgeAt(DateTimeOffset.UtcNow);
        StationAge = age.TotalMinutes < 90
            ? $"Messung vor {(int)age.TotalMinutes} min"
            : $"Messung {station.Timestamp.ToLocalTime():dd.MM. HH:mm}";

        StationTemperature = Unit(station.TemperatureC, "F1", "°C");
        StationHumidity = Unit(station.RelativeHumidityPercent, "F0", "%");
        StationPressure = Unit(station.PressureMslHpa, "F0", "hPa");

        StationWind = station.WindSpeedMs is { } wind
            ? $"{WindScale.MsToKmh(wind).ToString("F0", German)} km/h aus " +
              $"{WindScale.CompassPoint(station.WindDirectionDeg ?? 0)}"
            : "—";

        StationCaveat = station.IsRepresentative
            ? string.Empty
            : "Nächste Station weit entfernt — Messwerte nur bedingt auf die Einsatzstelle übertragbar.";

        // Where model and measurement disagree noticeably, say so: it is a hint
        // that local terrain or an inversion is doing something the model missed.
        if (station.TemperatureC is { } measured && snapshot.TemperatureC is { } modelled)
        {
            double delta = modelled - measured;
            ModelDeviation = Math.Abs(delta) >= 1.5
                ? $"Modell weicht um {delta.ToString("+0.0;-0.0", German)} K von der Station ab."
                : string.Empty;
        }
        else
        {
            ModelDeviation = string.Empty;
        }
    }

    /// <summary>
    /// Reads the DWD radar composite at the exact position: measured five-minute
    /// steps followed by the RV extrapolation, rather than a picture to eyeball.
    /// </summary>
    private async Task UpdateDwdRadarAsync(
        GeoPosition position,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        DwdRadarBars.Clear();

        try
        {
            RadarPointSeries series = await _brightSky
                .GetRadarSeriesAsync(position, cancellationToken: cancellationToken)
                .ConfigureAwait(true);

            if (series.IsEmpty)
            {
                HasDwdRadar = false;
                DwdRadarSummary = "Keine DWD-Radarwerte für diese Position (außerhalb der Radarabdeckung?).";
                return;
            }

            HasDwdRadar = true;

            double peak = Math.Max(0.5, series.PeakMillimetresPerHour);
            const double maxHeight = 46.0;

            foreach (RadarPointStep step in series.Steps)
            {
                DwdRadarBars.Add(new NowcastBar(
                    step.Time.ToLocalTime().ToString("HH:mm"),
                    // The bar model carries millimetres; the label divides back out.
                    step.MillimetresPerHour / 4.0,
                    Math.Max(2.0, step.MillimetresPerHour / peak * maxHeight),
                    !step.IsForecast && step.Time >= now.AddMinutes(-5)));
            }

            RadarPointStep? current = series.At(now);
            RadarPointStep? starts = series.FirstWetForecast;

            DwdRadarSummary = (current, starts) switch
            {
                ({ } c, _) when c.HasPrecipitation =>
                    $"Am Standort {c.MillimetresPerHour.ToString("F1", German)} mm/h · " +
                    $"Spitze im Zeitraum {series.PeakMillimetresPerHour.ToString("F1", German)} mm/h.",
                (_, { } s) =>
                    $"Trocken — Niederschlag laut Radar ab {s.Time.ToLocalTime():HH:mm} Uhr " +
                    $"({s.MillimetresPerHour.ToString("F1", German)} mm/h).",
                _ => "Radar zeigt am Standort keinen Niederschlag im Vorhersagezeitraum."
            };
        }
        catch (RadarProviderException ex)
        {
            HasDwdRadar = false;
            DwdRadarSummary = ex.Message;
        }
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
            WarningSource = _warnings switch
            {
                AggregateWarningProvider aggregate => $"Quelle: {aggregate.LastSourceLabel}",
                CompositeWarningProvider composite => $"Quelle: {composite.LastSourceLabel}",
                _ => string.Empty
            };
            WarningSummary = HasWarnings
                ? $"{Warnings.Count} amtliche Warnung(en) — höchste Stufe: {Warnings[0].LevelLabel}"
                : "Keine amtlichen Warnungen für diese Position.";

            // Deciding what is new happens after the list is on screen, so the
            // alert and the entry it refers to appear together.
            IReadOnlyList<WarningAlert> alerts = _monitor.Observe(warnings);

            if (alerts.Count > 0 && _settings.WarningAlertEnabled)
            {
                WarningsAlerted?.Invoke(alerts);
            }
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

        // -- wind outlook
        ApplyWindOutlook(a, now);

        // -- nowcast
        NowcastSummary = a.Outlook.Summary;
        BuildNowcastBars(a, now);
        BuildWindForecast(a, now);

        // -- hints
        Hints.Clear();
        foreach (TacticalHint hint in a.Hints)
        {
            Hints.Add(hint);
        }

        UpdateAge(now);
    }

    private void ApplyWindOutlook(TacticalAssessment a, DateTimeOffset now)
    {
        if (a.WindShift is { } shift)
        {
            WindShiftHeadline = $"dreht {shift.DirectionLabel} → {WindScale.CompassPoint(shift.ToDeg)}";
            WindShiftDetail = shift.Describe(now);
            WindShiftColour = shift.AbsoluteDeltaDeg >= 90 ? UiColour.Alarm : UiColour.Caution;
        }
        else
        {
            WindShiftHeadline = "richtungsstabil";
            WindShiftDetail = "Keine relevante Winddrehung in den nächsten 6 Stunden erwartet.";
            WindShiftColour = UiColour.Ok;
        }

        GustPeakLabel = a.GustPeak is { } peak ? peak.Describe(now) : string.Empty;
    }

    /// <summary>Hourly wind bars for the next half day, marking the shift point.</summary>
    private void BuildWindForecast(TacticalAssessment a, DateTimeOffset now)
    {
        WindForecast.Clear();

        var steps = a.Snapshot.Hourly
            .Where(h => h.Time >= now.AddHours(-1) && h.Time <= now.AddHours(12))
            .OrderBy(h => h.Time)
            .ToList();

        if (steps.Count == 0)
        {
            return;
        }

        // Scale both bars against the strongest gust so the pair stays comparable.
        double peak = Math.Max(2.0, steps.Max(h => Math.Max(h.WindGustMs ?? 0, h.WindSpeedMs ?? 0)));
        const double maxHeight = 46.0;

        foreach (HourlyStep step in steps)
        {
            double speed = step.WindSpeedMs ?? 0;
            double gust = Math.Max(speed, step.WindGustMs ?? 0);
            double direction = step.WindDirectionDeg ?? 0;

            WindForecast.Add(new WindForecastBar(
                step.Time.ToLocalTime().ToString("HH"),
                WindScale.MsToKmh(speed),
                WindScale.MsToKmh(gust),
                Math.Max(2.0, speed / peak * maxHeight),
                Math.Max(2.0, gust / peak * maxHeight),
                // Arrows point downwind, matching the compass rose above.
                WindScale.DownwindDirection(direction),
                WindScale.CompassPoint(direction),
                a.WindShift is { } shift && step.Time == shift.Time));
        }
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
