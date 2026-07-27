using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ElwMeteo.Core.Time;

namespace ElwMeteo.App.ViewModels;

/// <summary>
/// The clock block: wall clock, UTC, tactical date-time groups and the elapsed
/// time since the operation was declared started.
/// </summary>
public sealed partial class ClockViewModel : ObservableObject, IDisposable
{
    private readonly DispatcherTimer _timer;
    private DateTimeOffset? _operationStart;
    private TimeSpan _accumulated = TimeSpan.Zero;

    public ClockViewModel()
    {
        // 200 ms keeps the seconds digit from visibly stuttering without costing anything.
        _timer = new DispatcherTimer(DispatcherPriority.Normal)
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        _timer.Tick += (_, _) => Update();
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

    [ObservableProperty]
    private string _operationElapsed = "0:00:00";

    [ObservableProperty]
    private string _operationStartLabel = "—";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OperationButtonLabel))]
    private bool _isOperationRunning;

    public string OperationButtonLabel => IsOperationRunning ? "Stopp" : "Start";

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

        OperationElapsed = TacticalTime.FormatElapsed(CurrentElapsed(now));

        Tick?.Invoke(now);
    }

    private TimeSpan CurrentElapsed(DateTimeOffset now) =>
        _operationStart is { } start ? _accumulated + (now - start) : _accumulated;

    /// <summary>Starts or pauses the operation timer without losing the accumulated time.</summary>
    [RelayCommand]
    private void ToggleOperation()
    {
        DateTimeOffset now = DateTimeOffset.Now;

        if (IsOperationRunning)
        {
            _accumulated = CurrentElapsed(now);
            _operationStart = null;
            IsOperationRunning = false;
        }
        else
        {
            _operationStart = now;
            IsOperationRunning = true;

            // Only stamp the label on a fresh start, not when resuming.
            if (_accumulated == TimeSpan.Zero)
            {
                OperationStartLabel = $"{TacticalTime.FormatLocal(now)}  ({now:HH:mm})";
            }
        }

        Update();
    }

    [RelayCommand]
    private void ResetOperation()
    {
        _accumulated = TimeSpan.Zero;
        _operationStart = IsOperationRunning ? DateTimeOffset.Now : null;
        OperationStartLabel = IsOperationRunning
            ? $"{TacticalTime.FormatLocal(DateTimeOffset.Now)}  ({DateTimeOffset.Now:HH:mm})"
            : "—";
        Update();
    }

    public void Dispose() => _timer.Stop();
}
