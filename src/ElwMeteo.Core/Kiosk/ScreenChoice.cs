namespace ElwMeteo.Core.Kiosk;

/// <summary>
/// One display as the shell reports it. A plain record rather than a WPF or
/// Avalonia screen type, so the selection rules below can be tested without a
/// window manager — and so the two heads cannot drift apart on them.
/// </summary>
/// <param name="Id">
/// Stable-ish name of the output: the device name on Windows
/// (<c>\\.\DISPLAY2</c>), the connector on Linux (<c>HDMI-1</c>). Stored in the
/// settings file, which is why it is a string and not an index: screen 2 is a
/// different screen depending on which order the system enumerated them in
/// after the last reboot, whereas the connector a vehicle monitor is plugged
/// into does not move.
/// </param>
public sealed record ScreenInfo(
    string Id,
    string Label,
    int X,
    int Y,
    int Width,
    int Height,
    bool IsPrimary)
{
    /// <summary>What the settings page shows, e.g. "HDMI-1 — 1920×1080 (Hauptbildschirm)".</summary>
    public string Describe() =>
        Width <= 0 || Height <= 0
            // The "whatever the system calls primary" entry of the settings list
            // carries no geometry; "— 0×0" would be noise, not information.
            ? Label
            : $"{Label} — {Width}×{Height}{(IsPrimary ? " (Hauptbildschirm)" : string.Empty)}";

    /// <summary>Same as <see cref="Describe"/>, as a property so XAML can bind it.</summary>
    public string Display => Describe();
}

/// <summary>
/// Which display the window opens on.
///
/// The whole point of this living in the domain library is the last rule: when
/// the configured screen is not there any more, fall back to the primary one.
/// A vehicle laptop gets undocked, and a window positioned at the coordinates
/// of a monitor that is no longer connected is a window nobody can see, on a
/// machine nobody can get back without editing a JSON file. That failure is
/// silent, happens only on somebody else's hardware, and is exactly the kind a
/// test can pin down.
/// </summary>
public static class ScreenChoice
{
    /// <summary>
    /// Picks the display to use. Returns null only when the shell reported no
    /// screens at all, in which case the caller should leave the window where
    /// the system put it.
    /// </summary>
    public static ScreenInfo? Select(IReadOnlyList<ScreenInfo>? screens, string? preferredId)
    {
        if (screens is null || screens.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(preferredId))
        {
            ScreenInfo? exact = screens.FirstOrDefault(
                s => string.Equals(s.Id, preferredId, StringComparison.OrdinalIgnoreCase));

            if (exact is not null)
            {
                return exact;
            }
        }

        return Primary(screens);
    }

    /// <summary>
    /// True when the stored preference names a screen that is not connected, so
    /// the diagnostics page can say so instead of leaving the operator to wonder
    /// why the window came up on the laptop panel again.
    /// </summary>
    public static bool IsPreferenceMissing(IReadOnlyList<ScreenInfo>? screens, string? preferredId)
    {
        if (string.IsNullOrWhiteSpace(preferredId) || screens is null || screens.Count == 0)
        {
            return false;
        }

        return !screens.Any(s => string.Equals(s.Id, preferredId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The primary display, or the first one when none is flagged. Some X11
    /// setups report no primary at all; "first" is then as good an answer as
    /// exists, and better than no window.
    /// </summary>
    private static ScreenInfo Primary(IReadOnlyList<ScreenInfo> screens) =>
        screens.FirstOrDefault(s => s.IsPrimary) ?? screens[0];
}
