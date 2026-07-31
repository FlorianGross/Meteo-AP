using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ElwMeteo.Presentation.Platform;

/// <summary>
/// Marshals work onto the interface thread.
///
/// GPS fixes arrive on a serial thread and request records on whichever thread
/// the HTTP call finished on; both end up changing something the operator is
/// looking at. WPF and Avalonia each have their own idea of what that thread is,
/// so the view models ask through here instead of naming either.
/// </summary>
public interface IUiDispatcher
{
    bool IsOnUiThread { get; }

    /// <summary>Runs the action on the interface thread, now if already there.</summary>
    void Post(Action action);
}

/// <summary>A repeating timer that ticks on the interface thread.</summary>
public interface IUiTimer : IDisposable
{
    TimeSpan Interval { get; set; }

    bool IsRunning { get; }

    void Start();

    void Stop();
}

/// <summary>Creates <see cref="IUiTimer"/> instances; supplied by the shell.</summary>
public interface IUiTimerFactory
{
    IUiTimer Create(TimeSpan interval, Action onTick);
}

/// <summary>
/// Puts text on the clipboard. Both heads have one, neither has the same API,
/// and Avalonia's is asynchronous — so the contract is the narrow thing both
/// can honour.
/// </summary>
public interface IClipboardService
{
    /// <summary>True when the text was handed over; the clipboard can be locked.</summary>
    bool TrySetText(string text);
}

/// <summary>Opens a URL or a folder in whatever the system uses for it.</summary>
public interface IShellLauncher
{
    bool TryOpen(string target, out string? error);
}

/// <summary>
/// Makes a noise when a new warning arrives.
///
/// Deliberately allowed to fail. .NET has no cross-platform audio, so on some
/// machines there will be no sound at all — which is why the visible alert is
/// the real one and this is the addition. A feature that only works when the
/// speakers happen to be wired up must not be the only thing standing between
/// an operator and a severe weather warning.
/// </summary>
public interface IAlertSignal
{
    /// <summary>True when something was actually played.</summary>
    bool Sound();
}

/// <summary>
/// Opens a target through the operating system's own handler.
///
/// <c>UseShellExecute</c> is not Windows-only: on Linux .NET hands the target to
/// <c>xdg-open</c> and on macOS to <c>open</c>, which is exactly what is wanted
/// here. The explicit fallbacks below cover the case where that lookup fails on
/// a stripped-down system — a vehicle laptop without a desktop portal installed.
/// </summary>
public sealed class SystemShellLauncher : IShellLauncher
{
    public bool TryOpen(string target, out string? error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(target))
        {
            error = "Kein Ziel angegeben.";
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            if (TryFallback(target))
            {
                return true;
            }

            error = ex.Message;
            return false;
        }
    }

    private static bool TryFallback(string target)
    {
        string? opener =
            RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "xdg-open" :
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "open" :
            null;

        if (opener is null)
        {
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo(opener, target) { UseShellExecute = false });
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}

/// <summary>
/// Colour as plain numbers, so a view model never names a drawing type.
///
/// WPF and Avalonia both have a Color and a Brush and they are not the same
/// types. A view model that decides "this reading is alarming" is making a
/// meteorological judgement, not a rendering one — it says which colour, the
/// view turns that into a brush.
/// </summary>
public readonly record struct UiColour(byte R, byte G, byte B)
{
    public static UiColour FromRgb(byte r, byte g, byte b) => new(r, g, b);

    /// <summary>Parses "#RRGGBB"; falls back to grey rather than throwing.</summary>
    public static UiColour FromHex(string hex)
    {
        string value = hex.TrimStart('#');

        return value.Length == 6 &&
               byte.TryParse(value[..2], System.Globalization.NumberStyles.HexNumber, null, out byte r) &&
               byte.TryParse(value[2..4], System.Globalization.NumberStyles.HexNumber, null, out byte g) &&
               byte.TryParse(value[4..], System.Globalization.NumberStyles.HexNumber, null, out byte b)
            ? new UiColour(r, g, b)
            : Grey;
    }

    public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";

    // The application's semantic palette, kept in one place so the two heads
    // cannot drift apart.
    public static UiColour Ok => new(0x22, 0xC5, 0x5E);

    public static UiColour Caution => new(0xF5, 0x9E, 0x0B);

    public static UiColour Alarm => new(0xE6, 0x39, 0x46);

    public static UiColour Grey => new(0x8B, 0x94, 0xA3);
}
