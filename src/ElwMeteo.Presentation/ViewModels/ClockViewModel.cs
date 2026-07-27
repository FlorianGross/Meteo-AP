using CommunityToolkit.Mvvm.ComponentModel;
using ElwMeteo.Core.Time;
using ElwMeteo.Presentation.Platform;

namespace ElwMeteo.Presentation.ViewModels;

/// <summary>The clock block: wall clock, UTC and the tactical date-time groups.</summary>
public sealed partial class ClockViewModel : ObservableObject, IDisposable
{
    private readonly IUiTimer _timer;

    public ClockViewModel(IUiTimerFactory timers)
    {
        // 200 ms keeps the seconds digit from visibly stuttering without costing anything.
        _timer = timers.Create(TimeSpan.FromMilliseconds(200), Update);
        _timer.Start();

        Update();
    }

    [ObservableProperty]
    private string _localTime = "--:--:--";

    [ObservableProperty]
    private string _localDate = string.Empty;

    [ObservableProperty]
    private string _utcTime = "--:--:--";

    [ObservableProperty]
    private string _timeZoneLabel = string.Empty;

    /// <summary>Date-time group in local time, e.g. 271255BJUL26.</summary>
    [ObservableProperty]
    private string _tacticalLocal = string.Empty;

    /// <summary>Date-time group in UTC, e.g. 271055ZJUL26.</summary>
    [ObservableProperty]
    private string _tacticalZulu = string.Empty;

    /// <summary>Fired once per tick so dependent view models can refresh derived values.</summary>
    public event Action<DateTimeOffset>? Tick;

    private void Update()
    {
        DateTimeOffset now = DateTimeOffset.Now;

        LocalTime = now.ToString("HH:mm:ss");
        LocalDate = now.ToString("dddd, dd.MM.yyyy");
        UtcTime = now.ToUniversalTime().ToString("HH:mm:ss");
        TimeZoneLabel = TimeZoneInfo.Local.IsDaylightSavingTime(now) ? "MESZ (UTC+2)" : "MEZ (UTC+1)";

        TacticalLocal = TacticalTime.FormatLocal(now);
        TacticalZulu = TacticalTime.FormatZulu(now);

        Tick?.Invoke(now);
    }

    public void Dispose() => _timer.Stop();
}
