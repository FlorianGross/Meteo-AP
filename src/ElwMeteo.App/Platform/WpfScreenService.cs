using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using ElwMeteo.Core.Kiosk;
using ElwMeteo.Presentation.Platform;
using Forms = System.Windows.Forms;

namespace ElwMeteo.App.Platform;

/// <summary>
/// Display enumeration and window placement on Windows.
///
/// <see cref="Forms.Screen"/> rather than a hand-rolled <c>EnumDisplayMonitors</c>
/// binding: it is the same Win32 call with the marshalling already got right by
/// somebody else, which on a feature whose failure mode is "the window opens
/// where nobody can see it" is worth the reference to WinForms.
///
/// The hard part is not enumeration but units. The manifest declares
/// PerMonitorV2, so WinForms reports physical pixels while WPF positions windows
/// in device-independent units, and the factor between them differs per monitor —
/// a 100% laptop panel next to a 150% vehicle screen has no single conversion.
/// Rather than compute an answer that is right on the author's desk, the move is
/// attempted and then checked against the one authority that cannot be wrong:
/// Windows is asked which monitor the window actually ended up on.
/// </summary>
public sealed class WpfScreenService : IScreenService
{
    private Window? _window;

    private WindowStyle _styleBeforeFullScreen = WindowStyle.SingleBorderWindow;
    private WindowState _stateBeforeFullScreen = WindowState.Normal;
    private ResizeMode _resizeBeforeFullScreen = ResizeMode.CanResize;
    private bool _isFullScreen;

    public void Attach(Window window) => _window = window;

    public bool IsFullScreen => _isFullScreen;

    public IReadOnlyList<ScreenInfo> List()
    {
        try
        {
            return
            [
                .. Forms.Screen.AllScreens
                    .Select(screen => new ScreenInfo(
                        screen.DeviceName,
                        Label(screen),
                        screen.Bounds.X,
                        screen.Bounds.Y,
                        screen.Bounds.Width,
                        screen.Bounds.Height,
                        screen.Primary))
                    // Primary first, so a caller that takes the first entry is right.
                    .OrderByDescending(screen => screen.IsPrimary)
            ];
        }
        catch (Exception)
        {
            // A session without an interactive desktop — a service account, or an
            // RDP session that has gone away. No screens is the honest answer.
            return [];
        }
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
            // Out of full screen and out of maximised first: a window in either
            // state ignores a position change and would stay where it was.
            LeaveFullScreenInternal();
            _window.WindowState = WindowState.Normal;

            foreach (Point candidate in PlacementCandidates(screen))
            {
                _window.Left = candidate.X;
                _window.Top = candidate.Y;

                if (LandedOn(screen.Id))
                {
                    break;
                }
            }

            if (fullScreen)
            {
                EnterFullScreen();
            }

            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
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
            if (fullScreen)
            {
                EnterFullScreen();
            }
            else
            {
                LeaveFullScreenInternal();
            }

            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            error = ex.Message;
            return false;
        }
    }

    // ---------------------------------------------------------- full screen

    /// <summary>
    /// Maximised with no window chrome — the standard WPF full screen, and the
    /// one that also covers the task bar. Maximising follows whichever monitor
    /// the window is on, which is why placement happens first.
    /// </summary>
    private void EnterFullScreen()
    {
        if (_isFullScreen || _window is null)
        {
            return;
        }

        _styleBeforeFullScreen = _window.WindowStyle;
        _stateBeforeFullScreen = _window.WindowState;
        _resizeBeforeFullScreen = _window.ResizeMode;

        // Normal before None: changing the style of a maximised window leaves it
        // the size of the old client area rather than of the monitor.
        _window.WindowState = WindowState.Normal;
        _window.WindowStyle = WindowStyle.None;
        _window.ResizeMode = ResizeMode.NoResize;
        _window.WindowState = WindowState.Maximized;

        _isFullScreen = true;
    }

    private void LeaveFullScreenInternal()
    {
        if (!_isFullScreen || _window is null)
        {
            return;
        }

        _window.WindowState = WindowState.Normal;
        _window.WindowStyle = _styleBeforeFullScreen;
        _window.ResizeMode = _resizeBeforeFullScreen;

        // Not restored when it was full screen before: coming back to "maximised
        // without a title bar" is not leaving full screen.
        if (_stateBeforeFullScreen == WindowState.Maximized)
        {
            _window.WindowState = WindowState.Maximized;
        }

        _isFullScreen = false;
    }

    // ------------------------------------------------------------ placement

    /// <summary>
    /// Positions to try, in order: the physical top-left converted to
    /// device-independent units, then the raw physical value. On a single-scale
    /// setup — every vehicle installation with one monitor attached — the first
    /// is right. Where it is not, the second usually is, and
    /// <see cref="LandedOn"/> decides between them.
    /// </summary>
    private IEnumerable<Point> PlacementCandidates(ScreenInfo screen)
    {
        Point physical = new(screen.X, screen.Y);
        Point converted = ToDeviceIndependent(physical);

        yield return converted;

        if (converted != physical)
        {
            yield return physical;
        }
    }

    private Point ToDeviceIndependent(Point physical)
    {
        if (_window is null)
        {
            return physical;
        }

        // Null until the window has a handle, which is why placement is applied
        // from the Loaded handler and not from the constructor.
        System.Windows.Media.Matrix? transform =
            PresentationSource.FromVisual(_window)?.CompositionTarget?.TransformFromDevice;

        return transform is null ? physical : transform.Value.Transform(physical);
    }

    /// <summary>
    /// Whether the window is now on the intended monitor. Windows answers by the
    /// largest overlap, which is exactly the question being asked.
    /// </summary>
    private bool LandedOn(string deviceName)
    {
        if (_window is null)
        {
            return false;
        }

        try
        {
            IntPtr handle = new WindowInteropHelper(_window).Handle;

            if (handle == IntPtr.Zero)
            {
                // No handle yet, so nothing can be verified; take the first
                // candidate rather than moving the window twice for nothing.
                return true;
            }

            return string.Equals(
                Forms.Screen.FromHandle(handle).DeviceName,
                deviceName,
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return true;
        }
    }

    /// <summary>
    /// The device name without its Win32 prefix — <c>\\.\DISPLAY2</c> becomes
    /// <c>DISPLAY2</c>. The resolution and the primary flag are added by
    /// <see cref="ScreenInfo.Describe"/>, which is what the settings list shows:
    /// those are what somebody looking at two monitors can actually tell apart.
    /// </summary>
    private static string Label(Forms.Screen screen) =>
        screen.DeviceName.Replace(@"\\.\", string.Empty, StringComparison.Ordinal);
}
