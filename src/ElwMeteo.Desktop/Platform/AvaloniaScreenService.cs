using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using ElwMeteo.Core.Kiosk;
using ElwMeteo.Presentation.Platform;

namespace ElwMeteo.Desktop.Platform;

/// <summary>
/// Display enumeration and window placement through Avalonia's own
/// <see cref="Screens"/>.
///
/// The window is handed in lazily: the view model is built before the window
/// exists, and the composition root would otherwise have to construct the two
/// in the wrong order.
/// </summary>
public sealed class AvaloniaScreenService : IScreenService
{
    private Window? _window;

    public void Attach(Window window) => _window = window;

    public bool IsFullScreen => _window?.WindowState == WindowState.FullScreen;

    public IReadOnlyList<ScreenInfo> List()
    {
        Screens? screens = _window?.Screens;

        if (screens is null)
        {
            return [];
        }

        List<ScreenInfo> result = [];
        int index = 0;

        foreach (Screen screen in screens.All)
        {
            index++;

            // Avalonia exposes DisplayName only on some backends; the ordinal is
            // the fallback. Both go into the id, because the id is what ends up
            // in the settings file and a blank one would match every screen.
            string name = string.IsNullOrWhiteSpace(screen.DisplayName)
                ? $"Bildschirm {index}"
                : screen.DisplayName;

            result.Add(new ScreenInfo(
                name,
                name,
                screen.Bounds.X,
                screen.Bounds.Y,
                screen.Bounds.Width,
                screen.Bounds.Height,
                screen.IsPrimary));
        }

        // Primary first, so a caller that just takes the first entry is right.
        return [.. result.OrderByDescending(s => s.IsPrimary)];
    }

    public bool TryApply(ScreenInfo screen, bool fullScreen, out string? error)
    {
        error = null;

        if (_window is null)
        {
            error = "Das Fenster ist noch nicht bereit.";
            return false;
        }

        try
        {
            // Normal first: a window that is already maximised or full screen
            // ignores a position change, and would stay on the old display.
            _window.WindowState = WindowState.Normal;
            _window.Position = new PixelPoint(screen.X, screen.Y);

            if (fullScreen)
            {
                _window.WindowState = WindowState.FullScreen;
            }

            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            error = ex.Message;
            return false;
        }
    }

    public bool TrySetFullScreen(bool fullScreen, out string? error)
    {
        error = null;

        if (_window is null)
        {
            error = "Das Fenster ist noch nicht bereit.";
            return false;
        }

        try
        {
            _window.WindowState = fullScreen ? WindowState.FullScreen : WindowState.Normal;
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            error = ex.Message;
            return false;
        }
    }
}
