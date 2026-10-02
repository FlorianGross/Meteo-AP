using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using ElwMeteo.Core.Kiosk;
using ElwMeteo.Presentation.Platform;

namespace ElwMeteo.App.Platform;

/// <summary>
/// Display enumeration and window placement on Windows.
///
/// The monitors come from Win32 directly — <c>EnumDisplayMonitors</c> and
/// <c>GetMonitorInfoW</c>, the same calls <c>System.Windows.Forms.Screen</c>
/// makes. WinForms would have saved writing them, but switching
/// <c>UseWindowsForms</c> on brings <c>System.Drawing</c> and
/// <c>System.Windows.Forms</c> in as global usings, and in a WPF project that
/// makes <c>Point</c>, <c>Brush</c>, <c>Color</c>, <c>Pen</c>,
/// <c>UserControl</c> and <c>Application</c> ambiguous across files that have
/// nothing to do with screens. Four declarations here are a smaller change than
/// a second UI framework over the whole project.
///
/// The hard part is not enumeration but units. The manifest declares
/// PerMonitorV2, so Win32 reports physical pixels while WPF positions windows in
/// device-independent units, and the factor between them differs per monitor — a
/// 100% laptop panel next to a 150% vehicle screen has no single conversion.
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
            // Primary first, so a caller that takes the first entry is right.
            return [.. Monitors().OrderByDescending(screen => screen.IsPrimary)];
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
        // after Show and not from the constructor.
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

            IntPtr monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);

            return monitor != IntPtr.Zero &&
                   TryDescribe(monitor, out ScreenInfo? screen) &&
                   string.Equals(screen!.Id, deviceName, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return true;
        }
    }

    // ---------------------------------------------------------------- Win32

    private const int MonitorInfoPrimary = 0x1;
    private const uint MonitorDefaultToNearest = 0x2;

    /// <summary>Length of <c>MONITORINFOEXW.szDevice</c>, fixed by Win32 at CCHDEVICENAME.</summary>
    private const int DeviceNameLength = 32;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public int Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = DeviceNameLength)]
        public string DeviceName;
    }

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, ref NativeRect rect, IntPtr data);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(
        IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx info);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

    /// <summary>
    /// Every attached monitor, in the order Windows enumerates them.
    ///
    /// The whole monitor rectangle, not the work area: a full-screen window
    /// covers the task bar, and the work area would place it one task bar too
    /// far down on a screen that has one.
    /// </summary>
    private static List<ScreenInfo> Monitors()
    {
        List<ScreenInfo> found = [];

        // Held in a local for the duration of the call so the delegate cannot be
        // collected while native code holds the pointer to it.
        MonitorEnumProc callback = (IntPtr monitor, IntPtr hdc, ref NativeRect bounds, IntPtr data) =>
        {
            if (TryDescribe(monitor, out ScreenInfo? screen))
            {
                found.Add(screen!);
            }

            // Keep going; false would stop the enumeration at the first monitor.
            return true;
        };

        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
        GC.KeepAlive(callback);

        return found;
    }

    private static bool TryDescribe(IntPtr monitor, out ScreenInfo? screen)
    {
        screen = null;

        MonitorInfoEx info = new()
        {
            // Win32 rejects the call outright when this does not match the
            // struct it was handed, which is how it tells MONITORINFO from
            // MONITORINFOEX.
            Size = Marshal.SizeOf<MonitorInfoEx>(),
            DeviceName = string.Empty
        };

        if (!GetMonitorInfo(monitor, ref info))
        {
            return false;
        }

        // The name comes back NUL-padded to CCHDEVICENAME.
        string device = (info.DeviceName ?? string.Empty).TrimEnd('\0');

        if (string.IsNullOrEmpty(device))
        {
            // An id that matches nothing is worse than no entry: it would match
            // every screen whose name is also empty.
            return false;
        }

        screen = new ScreenInfo(
            device,
            Label(device),
            info.Monitor.Left,
            info.Monitor.Top,
            info.Monitor.Right - info.Monitor.Left,
            info.Monitor.Bottom - info.Monitor.Top,
            (info.Flags & MonitorInfoPrimary) != 0);

        return true;
    }

    /// <summary>
    /// The device name without its Win32 prefix — <c>\\.\DISPLAY2</c> becomes
    /// <c>DISPLAY2</c>. The resolution and the primary flag are added by
    /// <see cref="ScreenInfo.Describe"/>, which is what the settings list shows:
    /// those are what somebody looking at two monitors can actually tell apart.
    /// </summary>
    private static string Label(string deviceName) =>
        deviceName.Replace(@"\\.\", string.Empty, StringComparison.Ordinal);
}
