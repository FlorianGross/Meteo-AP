using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ElwMeteo.Core.Assessment;
using ElwMeteo.Core.Configuration;
using ElwMeteo.Core.Maps;
using ElwMeteo.Core.Meteorology;
using ElwMeteo.Core.Models;
using ElwMeteo.Core.Services;

namespace ElwMeteo.App.ViewModels;

/// <summary>A toggleable overlay in the layer panel.</summary>
public sealed partial class LayerToggle(MapLayerDefinition definition, bool isEnabled, Action onChanged)
    : ObservableObject
{
    public MapLayerDefinition Definition { get; } = definition;

    public string Title => Definition.Title;

    public string Group => Definition.Group;

    public string? Description => Definition.Description;

    private bool _isEnabled = isEnabled;

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (SetProperty(ref _isEnabled, value))
            {
                onChanged();
            }
        }
    }
}

/// <summary>Tab 2 — the map with radar animation, DWD overlays and the hazard cone.</summary>
public sealed partial class MapViewModel : ObservableObject, IDisposable
{
    private readonly RainViewerProvider _radar;
    private readonly WindFieldProvider _windFieldProvider;
    private readonly IWeatherProvider _weather;
    private readonly AppSettings _settings;
    private readonly DispatcherTimer _animationTimer;

    private RadarTimeline _timeline = RadarTimeline.Empty;
    private WindField _windField = WindField.Empty;
    private TacticalAssessment? _assessment;
    private bool _suppressPush;
    private CancellationTokenSource? _pointQuery;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public MapViewModel(
        RainViewerProvider radar,
        WindFieldProvider windFieldProvider,
        IWeatherProvider weather,
        AppSettings settings)
    {
        _radar = radar;
        _windFieldProvider = windFieldProvider;
        _weather = weather;
        _settings = settings;

        _animationTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(Math.Max(100, settings.RadarFrameDelayMs))
        };
        _animationTimer.Tick += (_, _) => AdvanceFrame();

        _suppressPush = true;
        try
        {
            foreach (MapLayerDefinition definition in MapLayerCatalog.Overlays)
            {
                Overlays.Add(new LayerToggle(
                    definition,
                    settings.EnabledOverlayIds.Contains(definition.Id),
                    PushState));
            }

            foreach (MapLayerDefinition definition in MapLayerCatalog.BaseLayers)
            {
                BaseLayers.Add(definition);
            }

            ApplyLayerFilter();

            SelectedBaseLayer = BaseLayers.FirstOrDefault(l => l.Id == settings.SelectedBaseLayerId)
                                ?? BaseLayers.FirstOrDefault();

            ShowHazardCone = settings.ShowHazardCone;
            HazardRangeMetres = settings.HazardRangeMetresOverride ?? 0;

            SelectedColourScheme = RadarTimeline.ColourSchemes
                .FirstOrDefault(c => c.Id == settings.RadarColourScheme)
                ?? RadarTimeline.ColourSchemes[4];
            ShowSnow = settings.RadarShowSnow;
            ShowSatellite = settings.ShowSatellite;
            ShowWindField = settings.ShowWindField;
            ShowWindAnimation = settings.ShowWindAnimation;
            WindFieldGridSize = settings.WindFieldGridSize;
            WindFieldSpacingMetres = settings.WindFieldSpacingMetres;
        }
        finally
        {
            _suppressPush = false;
        }
    }

    /// <summary>Raised with the JSON state the WebView page should apply.</summary>
    public event Action<string>? StateChanged;

    public ObservableCollection<LayerToggle> Overlays { get; } = [];

    /// <summary>Overlays narrowed by <see cref="LayerFilter"/>; the panel binds to this.</summary>
    public ObservableCollection<LayerToggle> VisibleOverlays { get; } = [];

    /// <summary>Free-text filter over overlay title, group and description.</summary>
    [ObservableProperty]
    private string _layerFilter = string.Empty;

    public ObservableCollection<MapLayerDefinition> BaseLayers { get; } = [];

    [ObservableProperty]
    private MapLayerDefinition? _selectedBaseLayer;

    [ObservableProperty]
    private bool _showHazardCone = true;

    /// <summary>Downwind extent in metres; 0 means "follow the stability class".</summary>
    [ObservableProperty]
    private double _hazardRangeMetres;

    [ObservableProperty]
    private bool _showRadar = true;

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    private int _frameIndex;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MaxFrameIndex))]
    private int _frameCount;

    /// <summary>Upper bound for the timeline slider — the last valid frame index.</summary>
    public int MaxFrameIndex => Math.Max(0, FrameCount - 1);

    [ObservableProperty]
    private string _frameLabel = "—";

    [ObservableProperty]
    private string _radarStatus = "Radar noch nicht geladen.";

    [ObservableProperty]
    private double _radarOpacity = 0.75;

    [ObservableProperty]
    private bool _followPosition = true;

    [ObservableProperty]
    private string _clickedPositionLabel = string.Empty;

    // ------------------------------------------------------- radar styling

    public IReadOnlyList<RadarColourScheme> ColourSchemes => RadarTimeline.ColourSchemes;

    [ObservableProperty]
    private RadarColourScheme? _selectedColourScheme;

    [ObservableProperty]
    private bool _showSnow = true;

    [ObservableProperty]
    private bool _smoothRadar = true;

    // ---------------------------------------------------------- satellite

    [ObservableProperty]
    private bool _showSatellite;

    [ObservableProperty]
    private double _satelliteOpacity = 0.5;

    [ObservableProperty]
    private string _satelliteStatus = string.Empty;

    // --------------------------------------------------------- wind field

    [ObservableProperty]
    private bool _showWindField;

    /// <summary>Windy-style particle animation driven by the same grid.</summary>
    [ObservableProperty]
    private bool _showWindAnimation;

    /// <summary>Nodes per side of the wind grid.</summary>
    [ObservableProperty]
    private int _windFieldGridSize = 5;

    [ObservableProperty]
    private double _windFieldSpacingMetres = 2000;

    [ObservableProperty]
    private string _windFieldStatus = "Windfeld nicht geladen.";

    [ObservableProperty]
    private bool _isWindFieldLoading;

    // --------------------------------------------------- clicked-point info

    [ObservableProperty]
    private string _clickedWeatherLabel = string.Empty;

    [ObservableProperty]
    private bool _isPointQueryRunning;

    // -------------------------------------------------------- property hooks

    partial void OnSelectedBaseLayerChanged(MapLayerDefinition? value)
    {
        if (value is not null)
        {
            _settings.SelectedBaseLayerId = value.Id;
        }

        PushState();
    }

    partial void OnShowHazardConeChanged(bool value)
    {
        _settings.ShowHazardCone = value;
        PushState();
    }

    partial void OnHazardRangeMetresChanged(double value)
    {
        _settings.HazardRangeMetresOverride = value <= 0 ? null : value;
        PushState();
    }

    partial void OnLayerFilterChanged(string value) => ApplyLayerFilter();

    /// <summary>
    /// Narrows the overlay list. Matching on group and description too means
    /// "wind", "brand" or "warn" find the right layers without knowing their names.
    /// </summary>
    private void ApplyLayerFilter()
    {
        string needle = LayerFilter.Trim();

        VisibleOverlays.Clear();
        foreach (LayerToggle toggle in Overlays)
        {
            bool matches = needle.Length == 0 ||
                Contains(toggle.Title, needle) ||
                Contains(toggle.Group, needle) ||
                Contains(toggle.Description, needle);

            if (matches)
            {
                VisibleOverlays.Add(toggle);
            }
        }

        static bool Contains(string? haystack, string needle) =>
            haystack is not null && haystack.Contains(needle, StringComparison.CurrentCultureIgnoreCase);
    }

    partial void OnShowRadarChanged(bool value) => PushState();

    partial void OnRadarOpacityChanged(double value) => PushState();

    partial void OnSelectedColourSchemeChanged(RadarColourScheme? value)
    {
        if (value is not null)
        {
            _settings.RadarColourScheme = value.Id;
        }

        PushState();
    }

    partial void OnShowSnowChanged(bool value)
    {
        _settings.RadarShowSnow = value;
        PushState();
    }

    partial void OnSmoothRadarChanged(bool value) => PushState();

    partial void OnShowSatelliteChanged(bool value)
    {
        _settings.ShowSatellite = value;
        PushState();
    }

    partial void OnSatelliteOpacityChanged(double value) => PushState();

    partial void OnShowWindFieldChanged(bool value)
    {
        _settings.ShowWindField = value;

        if (value && _windField.IsEmpty)
        {
            _ = RefreshWindFieldCommand.ExecuteAsync(null);
            return;
        }

        PushState();
    }

    partial void OnShowWindAnimationChanged(bool value)
    {
        _settings.ShowWindAnimation = value;

        if (value && _windField.IsEmpty)
        {
            _ = RefreshWindFieldCommand.ExecuteAsync(null);
            return;
        }

        PushState();
    }

    partial void OnWindFieldGridSizeChanged(int value) => _settings.WindFieldGridSize = value;

    partial void OnWindFieldSpacingMetresChanged(double value) => _settings.WindFieldSpacingMetres = value;

    partial void OnFrameIndexChanged(int value)
    {
        UpdateFrameLabel();
        PushState();
    }

    partial void OnIsPlayingChanged(bool value)
    {
        if (value && FrameCount > 1)
        {
            _animationTimer.Start();
        }
        else
        {
            _animationTimer.Stop();
        }
    }

    // ------------------------------------------------------------- commands

    [RelayCommand]
    private async Task RefreshRadarAsync(CancellationToken cancellationToken)
    {
        try
        {
            RadarStatus = "Radarbilder werden geladen …";
            _timeline = await _radar.GetTimelineAsync(cancellationToken).ConfigureAwait(true);

            FrameCount = _timeline.Frames.Count;

            if (FrameCount == 0)
            {
                RadarStatus = "Keine Radarbilder verfügbar.";
                return;
            }

            // Land on the most recent observation, not on the oldest frame or a forecast.
            int lastObserved = _timeline.Frames
                .Select((frame, index) => (frame, index))
                .Where(item => !item.frame.IsForecast)
                .Select(item => item.index)
                .DefaultIfEmpty(0)
                .Max();

            FrameIndex = lastObserved;

            int forecastCount = _timeline.Forecast.Count();
            RadarStatus = $"{FrameCount} Bilder · {FrameCount - forecastCount} Messung, {forecastCount} Vorhersage";

            UpdateFrameLabel();
            PushState();
        }
        catch (RadarProviderException ex)
        {
            RadarStatus = ex.Message;
        }
        catch (OperationCanceledException)
        {
            RadarStatus = "Radarabruf abgebrochen.";
        }
    }

    /// <summary>
    /// Fetches the wind grid around the current position. One request covers the
    /// whole grid, so this is cheap enough to refresh with the radar.
    /// </summary>
    [RelayCommand]
    private async Task RefreshWindFieldAsync(CancellationToken cancellationToken)
    {
        if (_assessment is null)
        {
            WindFieldStatus = "Erst Position und Wetter abrufen.";
            return;
        }

        IsWindFieldLoading = true;
        try
        {
            WindFieldStatus = "Windfeld wird geladen …";

            _windField = await _windFieldProvider.GetAsync(
                    _assessment.Snapshot.Position.ToLatLon(),
                    WindFieldGridSize,
                    WindFieldSpacingMetres,
                    cancellationToken)
                .ConfigureAwait(true);

            int withData = _windField.Points.Count(p => p.HasData);
            string spread = _windField.DirectionSpreadDeg is { } degrees
                ? $" · Richtungsspreizung {degrees:F0}°"
                : string.Empty;

            WindFieldStatus = $"{withData} von {_windField.Points.Count} Gitterpunkten{spread}";

            // A wide spread means the terrain is steering the flow and the single
            // cone drawn from the vehicle's reading is not the whole story.
            if (_windField.DirectionSpreadDeg is >= 60)
            {
                WindFieldStatus += " — uneinheitliche Strömung, Ausbreitungskegel kritisch bewerten.";
            }

            PushState();
        }
        catch (WeatherProviderException ex)
        {
            WindFieldStatus = ex.Message;
        }
        catch (OperationCanceledException)
        {
            WindFieldStatus = "Windfeldabruf abgebrochen.";
        }
        finally
        {
            IsWindFieldLoading = false;
        }
    }

    /// <summary>Turns every overlay off — the fastest way back to a clean map.</summary>
    [RelayCommand]
    private void ClearOverlays()
    {
        foreach (LayerToggle toggle in Overlays)
        {
            toggle.IsEnabled = false;
        }
    }

    [RelayCommand]
    private void ResetLayerFilter() => LayerFilter = string.Empty;

    [RelayCommand]
    private void ClearClickedPoint()
    {
        ClickedPositionLabel = string.Empty;
        ClickedWeatherLabel = string.Empty;
    }

    [RelayCommand]
    private void TogglePlay() => IsPlaying = !IsPlaying;

    [RelayCommand]
    private void StepBack()
    {
        IsPlaying = false;
        if (FrameCount > 0)
        {
            FrameIndex = (FrameIndex - 1 + FrameCount) % FrameCount;
        }
    }

    [RelayCommand]
    private void StepForward()
    {
        IsPlaying = false;
        if (FrameCount > 0)
        {
            FrameIndex = (FrameIndex + 1) % FrameCount;
        }
    }

    /// <summary>Jumps to the newest observed frame — the "what is happening right now" view.</summary>
    [RelayCommand]
    private void JumpToNow()
    {
        IsPlaying = false;

        int index = _timeline.Frames
            .Select((frame, i) => (frame, i))
            .Where(item => !item.frame.IsForecast)
            .Select(item => item.i)
            .DefaultIfEmpty(0)
            .Max();

        FrameIndex = index;
    }

    [RelayCommand]
    private void RecentreOnPosition()
    {
        FollowPosition = true;
        PushState(recentre: true);
    }

    // -------------------------------------------------------------- updates

    /// <summary>Called by the dashboard whenever a fresh assessment arrives.</summary>
    public void ApplyAssessment(TacticalAssessment assessment)
    {
        _assessment = assessment;
        PushState(recentre: FollowPosition);
    }

    /// <summary>Called by the view when the user clicks the map.</summary>
    public void ReportMapClick(double latitude, double longitude)
    {
        var point = new LatLon(latitude, longitude);
        string label = Geodesy.FormatDegreesDecimalMinutes(point);

        if (_assessment is not null)
        {
            double distance = Geodesy.DistanceMetres(_assessment.Snapshot.Position.ToLatLon(), point);
            double bearing = Geodesy.BearingDeg(_assessment.Snapshot.Position.ToLatLon(), point);
            label += $"  ·  {distance:F0} m / {bearing:F0}° vom Standort";
        }

        ClickedPositionLabel = label;

        _ = QueryPointWeatherAsync(point);
    }

    /// <summary>
    /// Fetches conditions at a clicked point — useful for checking the weather
    /// over a staging area or an evacuation destination before committing to it.
    /// A click while a query is in flight cancels the older one.
    /// </summary>
    private async Task QueryPointWeatherAsync(LatLon point)
    {
        CancellationTokenSource? previous = _pointQuery;
        var source = new CancellationTokenSource();
        _pointQuery = source;
        previous?.Cancel();
        previous?.Dispose();

        IsPointQueryRunning = true;
        ClickedWeatherLabel = "Wetter am Punkt wird abgerufen …";

        try
        {
            var position = new GeoPosition(
                point.Latitude, point.Longitude, PositionSource.Manual, DateTimeOffset.UtcNow);

            WeatherSnapshot snapshot = await _weather.GetAsync(position, source.Token).ConfigureAwait(true);

            string temperature = snapshot.TemperatureC is { } t ? $"{t:F1} °C" : "—";
            string wind = snapshot.WindSpeedMs is { } w
                ? $"{WindScale.MsToKmh(w):F0} km/h aus {WindScale.CompassPoint(snapshot.WindDirectionDeg ?? 0)}"
                : "—";
            string gust = snapshot.WindGustMs is { } g ? $", Böen {WindScale.MsToKmh(g):F0} km/h" : string.Empty;
            string rain = snapshot.PrecipitationMm is { } p and > 0 ? $", Niederschlag {p:F1} mm/h" : string.Empty;

            ClickedWeatherLabel =
                $"{WeatherCodes.Describe(snapshot.WeatherCode)} · {temperature} · Wind {wind}{gust}{rain}";
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer click; the newer query owns the label now.
        }
        catch (Exception ex)
        {
            ClickedWeatherLabel = $"Wetter am Punkt nicht abrufbar: {ex.Message}";
        }
        finally
        {
            if (ReferenceEquals(_pointQuery, source))
            {
                IsPointQueryRunning = false;
                _pointQuery = null;
                source.Dispose();
            }
        }
    }

    /// <summary>Called by the view once the page has loaded and is ready for state.</summary>
    public void NotifyPageReady() => PushState(recentre: true);

    private void AdvanceFrame()
    {
        if (FrameCount == 0)
        {
            return;
        }

        // Pause briefly on the last frame so the forecast end is readable.
        int next = (FrameIndex + 1) % FrameCount;
        FrameIndex = next;
    }

    private void UpdateFrameLabel()
    {
        if (FrameIndex < 0 || FrameIndex >= _timeline.Frames.Count)
        {
            FrameLabel = "—";
            return;
        }

        RadarFrame frame = _timeline.Frames[FrameIndex];
        string kind = frame.IsForecast ? "Vorhersage" : "Messung";
        FrameLabel = $"{frame.TimeLabel}  ({frame.RelativeLabel(DateTimeOffset.Now)}, {kind})";
    }

    private void PushState() => PushState(recentre: false);

    private void PushState(bool recentre)
    {
        if (_suppressPush)
        {
            return;
        }

        _settings.EnabledOverlayIds = Overlays.Where(o => o.IsEnabled).Select(o => o.Definition.Id).ToList();

        StateChanged?.Invoke(JsonSerializer.Serialize(BuildState(recentre), JsonOptions));
    }

    private MapState BuildState(bool recentre)
    {
        var layers = new List<MapLayerState>();

        if (SelectedBaseLayer is not null)
        {
            layers.Add(MapLayerState.From(SelectedBaseLayer, enabled: true));
        }

        foreach (LayerToggle toggle in Overlays)
        {
            layers.Add(MapLayerState.From(toggle.Definition, toggle.IsEnabled));
        }

        string? radarTileUrl = null;
        string? satelliteTileUrl = null;

        if (FrameIndex >= 0 && FrameIndex < _timeline.Frames.Count)
        {
            RadarFrame frame = _timeline.Frames[FrameIndex];

            if (ShowRadar)
            {
                radarTileUrl = _timeline.TileUrlTemplate(
                    frame,
                    SelectedColourScheme?.Id ?? 4,
                    SmoothRadar,
                    ShowSnow);
            }

            // Satellite follows the radar clock so both animate together.
            if (ShowSatellite && _timeline.SatelliteFrameNear(frame.Time) is { } satelliteFrame)
            {
                satelliteTileUrl = _timeline.SatelliteTileUrlTemplate(satelliteFrame);
            }
        }

        // The animation needs the field as a regular grid of components; the
        // arrows need it as discrete points. Both come from the same fetch.
        WindGridState? windGrid = null;
        if (ShowWindAnimation && _windField.ToGrid() is { } grid)
        {
            windGrid = new WindGridState(
                grid.Size, grid.North, grid.South, grid.West, grid.East,
                grid.U, grid.V, grid.MaxSpeedMs);
        }

        var windArrows = new List<WindArrowState>();
        if (ShowWindField)
        {
            foreach (WindFieldPoint point in _windField.Points.Where(p => p.HasData))
            {
                windArrows.Add(new WindArrowState(
                    point.Latitude,
                    point.Longitude,
                    point.DownwindDeg ?? 0,
                    point.SpeedMs ?? 0,
                    WindScale.MsToKmh(point.SpeedMs ?? 0),
                    point.GustMs is { } gust ? WindScale.MsToKmh(gust) : null,
                    WindScale.CompassPoint(point.DirectionDeg ?? 0)));
            }
        }

        MarkerState? marker = null;
        HazardState? hazard = null;

        if (_assessment is not null)
        {
            var snapshot = _assessment.Snapshot;
            LatLon origin = snapshot.Position.ToLatLon();

            marker = new MarkerState(
                origin.Latitude,
                origin.Longitude,
                snapshot.Position.SourceLabel,
                snapshot.TemperatureC,
                snapshot.WindSpeedMs,
                snapshot.WindDirectionDeg,
                _assessment.WindFromCompass,
                _assessment.DownwindBearingDeg);

            if (ShowHazardCone && snapshot.WindDirectionDeg is not null)
            {
                HazardArea area = HazardPlume.Build(
                    origin,
                    snapshot.WindDirectionDeg.Value,
                    _assessment.Stability.Pasquill,
                    HazardRangeMetres <= 0 ? null : HazardRangeMetres,
                    _settings.HazardInnerRadiusMetres);

                hazard = new HazardState(
                    area.ConeOutline.Select(p => new[] { p.Latitude, p.Longitude }).ToList(),
                    area.CentreLine.Select(p => new[] { p.Latitude, p.Longitude }).ToList(),
                    area.InnerRadiusMetres,
                    area.RangeMetres,
                    _assessment.Stability.KlugManier,
                    _assessment.DownwindCompass);
            }
        }

        return new MapState(
            layers,
            radarTileUrl,
            RadarOpacity,
            satelliteTileUrl,
            SatelliteOpacity,
            windArrows,
            windGrid,
            marker,
            hazard,
            recentre,
            _settings.MapZoom);
    }

    public void Dispose() => _animationTimer.Stop();

    // ------------------------------------------------- state pushed to the page

    private sealed record MapState(
        IReadOnlyList<MapLayerState> Layers,
        string? RadarTileUrl,
        double RadarOpacity,
        string? SatelliteTileUrl,
        double SatelliteOpacity,
        IReadOnlyList<WindArrowState> WindArrows,
        WindGridState? WindGrid,
        MarkerState? Marker,
        HazardState? Hazard,
        bool Recentre,
        double Zoom);

    private sealed record WindGridState(
        int Size,
        double North,
        double South,
        double West,
        double East,
        double[] U,
        double[] V,
        double MaxSpeedMs);

    private sealed record WindArrowState(
        double Latitude,
        double Longitude,
        double DownwindDeg,
        double SpeedMs,
        double SpeedKmh,
        double? GustKmh,
        string FromCompass);

    private sealed record MapLayerState(
        string Id,
        string Title,
        string Kind,
        bool Enabled,
        string? TileUrl,
        string? WmsUrl,
        string? WmsLayers,
        string? WmsFormat,
        bool WmsTransparent,
        string Attribution,
        double Opacity,
        int MaxZoom)
    {
        public static MapLayerState From(MapLayerDefinition definition, bool enabled) => new(
            definition.Id,
            definition.Title,
            definition.Kind == MapLayerKind.Base ? "base" : "overlay",
            enabled,
            definition.TileUrl,
            definition.WmsUrl,
            definition.WmsLayers,
            definition.WmsFormat,
            definition.WmsTransparent,
            definition.Attribution,
            definition.Opacity,
            definition.MaxZoom);
    }

    private sealed record MarkerState(
        double Latitude,
        double Longitude,
        string SourceLabel,
        double? TemperatureC,
        double? WindSpeedMs,
        double? WindDirectionDeg,
        string WindFromCompass,
        double DownwindBearingDeg);

    private sealed record HazardState(
        IReadOnlyList<double[]> Cone,
        IReadOnlyList<double[]> CentreLine,
        double InnerRadiusMetres,
        double RangeMetres,
        string StabilityClass,
        string DownwindCompass);
}
