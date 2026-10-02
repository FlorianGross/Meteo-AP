using ElwMeteo.Core.Kiosk;

namespace ElwMeteo.Presentation.Platform;

/// <summary>
/// The displays attached to the machine, and moving the window onto one of them.
///
/// Both heads can answer this and neither does it the same way — WPF needs
/// WinForms' <c>Screen.AllScreens</c>, Avalonia carries a <c>Screens</c>
/// collection on the window. Which display the window belongs on is a decision
/// (<see cref="ScreenChoice"/>); putting it there is a rendering act, and that
/// is what this is for.
/// </summary>
public interface IScreenService
{
    /// <summary>Everything the shell currently reports, primary first where known.</summary>
    IReadOnlyList<ScreenInfo> List();

    /// <summary>
    /// Moves the main window onto that display and, when asked, fills it.
    /// Returns false with a reason rather than throwing — a screen that was
    /// unplugged between the listing and the move is a thing to report.
    /// </summary>
    bool TryApply(ScreenInfo screen, bool fullScreen, out string? error);

    /// <summary>Full screen on or off on whichever display the window is on now.</summary>
    bool TrySetFullScreen(bool fullScreen, out string? error);

    /// <summary>Whether the window is currently full screen.</summary>
    bool IsFullScreen { get; }
}

/// <summary>Stands in before a window exists, and in the tests.</summary>
public sealed class NoScreenService : IScreenService
{
    public IReadOnlyList<ScreenInfo> List() => [];

    public bool IsFullScreen => false;

    public bool TryApply(ScreenInfo screen, bool fullScreen, out string? error)
    {
        error = "Bildschirmzuordnung ist in dieser Umgebung nicht verfügbar.";
        return false;
    }

    public bool TrySetFullScreen(bool fullScreen, out string? error)
    {
        error = "Vollbild ist in dieser Umgebung nicht verfügbar.";
        return false;
    }
}
