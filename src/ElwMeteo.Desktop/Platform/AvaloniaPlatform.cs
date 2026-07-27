using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Threading;
using ElwMeteo.Presentation.Platform;

namespace ElwMeteo.Desktop.Platform;

/// <summary>Avalonia's dispatcher behind the shared interface.</summary>
public sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    public bool IsOnUiThread => Dispatcher.UIThread.CheckAccess();

    public void Post(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
        }
        else
        {
            Dispatcher.UIThread.Post(action);
        }
    }
}

public sealed class AvaloniaTimer : IUiTimer
{
    private readonly DispatcherTimer _timer;

    public AvaloniaTimer(TimeSpan interval, Action onTick)
    {
        _timer = new DispatcherTimer { Interval = interval };
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

public sealed class AvaloniaTimerFactory : IUiTimerFactory
{
    public IUiTimer Create(TimeSpan interval, Action onTick) => new AvaloniaTimer(interval, onTick);
}

/// <summary>
/// Avalonia's clipboard is asynchronous and hangs off the top-level window.
///
/// The shared contract is synchronous because WPF's is, and because a view model
/// only ever wants to know whether the text got there. The copy is started and
/// reported as accepted; if the platform later refuses, the operator sees it in
/// the clipboard, not in a dialog.
/// </summary>
public sealed class AvaloniaClipboard : IClipboardService
{
    public bool TrySetText(string text)
    {
        try
        {
            if (global::Avalonia.Application.Current?.ApplicationLifetime
                is not IClassicDesktopStyleApplicationLifetime { MainWindow: { } window })
            {
                return false;
            }

            IClipboard? clipboard = window.Clipboard;

            if (clipboard is null)
            {
                return false;
            }

            // Fire and forget: on Wayland the write can only complete once the
            // compositor asks for the data, which may be after this returns.
            _ = clipboard.SetTextAsync(text);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}

/// <summary>Turns the view models' plain colours into Avalonia brushes.</summary>
public static class UiColourExtensions
{
    public static Color ToColor(this UiColour colour) => Color.FromRgb(colour.R, colour.G, colour.B);

    public static IBrush ToBrush(this UiColour colour) => new SolidColorBrush(colour.ToColor());
}
