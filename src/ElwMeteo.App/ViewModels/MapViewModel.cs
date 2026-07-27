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
    private readonly AppSettings _settings;
    private readonly DispatcherTimer _animationTimer;

    private RadarTimeline _timeline = RadarTimeline.Empty;
    private TacticalAssessment? _assessment;
    private bool _suppressPush;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public MapViewModel(RainViewerProvider radar, AppSettings settings)
    {
        _radar = radar;
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

            SelectedBaseLayer = BaseLayers.FirstOrDefault(l => l.Id == settings.SelectedBaseLayerId)
                                ?? BaseLayers.FirstOrDefault();

            ShowHazardCone = settings.ShowHazardCone;
            HazardRangeMetres = settings.HazardRangeMetresOverride ?? 0;
        }
        finally
        {
            _suppressPush = false;
        }
    }

    /// <summary>Raised with the JSON state the WebView page should apply.</summary>
    public event Action<string>? StateChanged;

    public ObservableCollection<LayerToggle> Overlays { get; } = [];

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

    partial void OnShowRadarChanged(bool value) => PushState();

    partial void OnRadarOpacityChanged(double value) => PushState();

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
        if (ShowRadar && FrameIndex >= 0 && FrameIndex < _timeline.Frames.Count)
        {
            radarTileUrl = _timeline.TileUrlTemplate(_timeline.Frames[FrameIndex]);
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
        MarkerState? Marker,
        HazardState? Hazard,
        bool Recentre,
        double Zoom);

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
