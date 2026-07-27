using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using ElwMeteo.Presentation.Platform;

namespace ElwMeteo.App.Platform;

/// <summary>WPF's dispatcher behind the shared interface.</summary>
public sealed class WpfDispatcher : IUiDispatcher
{
    private readonly Dispatcher _dispatcher = Application.Current?.Dispatcher
                                              ?? Dispatcher.CurrentDispatcher;

    public bool IsOnUiThread => _dispatcher.CheckAccess();

    public void Post(Action action)
    {
        if (_dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            _dispatcher.BeginInvoke(action);
        }
    }
}

public sealed class WpfTimer : IUiTimer
{
    private readonly DispatcherTimer _timer;

    public WpfTimer(TimeSpan interval, Action onTick)
    {
        _timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = interval };
        _timer.Tick += (_, _) => onTick();
    }

    public TimeSpan Interval
    {
        get => _timer.Interval;
        set => _timer.Interval = value;
    }

    public bool IsRunning => _timer.IsEnabled;

    public void Start() => _timer.Start();

    public void Stop() => _timer.Stop();

    public void Dispose() => _timer.Stop();
}

public sealed class WpfTimerFactory : IUiTimerFactory
{
    public IUiTimer Create(TimeSpan interval, Action onTick) => new WpfTimer(interval, onTick);
}

public sealed class WpfClipboard : IClipboardService
{
    public bool TrySetText(string text)
    {
        try
        {
            Clipboard.SetText(text);
            return true;
        }
        catch (Exception)
        {
            // The clipboard is shared and another process can hold it open.
            return false;
        }
    }
}

/// <summary>Turns the view models' plain colours into frozen WPF brushes.</summary>
public static class UiColourExtensions
{
    public static Color ToColor(this UiColour colour) => Color.FromRgb(colour.R, colour.G, colour.B);

    /// <summary>Frozen so the brush can cross threads and needs no change tracking.</summary>
    public static Brush ToBrush(this UiColour colour)
    {
        var brush = new SolidColorBrush(colour.ToColor());
        brush.Freeze();
        return brush;
    }
}
