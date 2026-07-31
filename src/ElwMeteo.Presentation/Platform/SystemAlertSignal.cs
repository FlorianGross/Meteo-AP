using System.Diagnostics;
using System.Runtime.Versioning;

namespace ElwMeteo.Presentation.Platform;

/// <summary>
/// Plays an alert tone through whatever the system happens to provide.
///
/// There is no portable way to do this. .NET ships no cross-platform audio, so
/// each system gets its own attempt: the desktop sound daemon on Linux, the
/// built-in player on macOS, the console beep on Windows. Every one of them can
/// legitimately be absent — a vehicle laptop with no sound server running is a
/// normal machine, not a broken one.
///
/// So the contract is „try, report whether it worked, never throw". The caller
/// shows the visible alert regardless and can tell the operator that the tone
/// did not come out, which is far better than a silent failure on the one
/// feature whose entire job is not being silent.
/// </summary>
public sealed class SystemAlertSignal : IAlertSignal
{
    /// <summary>Set once a player has been found, so later alerts skip the search.</summary>
    private (string Program, string Arguments)? _known;

    private bool _searched;

    public bool Sound()
    {
        if (OperatingSystem.IsWindows())
        {
            return WindowsBeep();
        }

        if (_known is { } known)
        {
            return TryRun(known.Program, known.Arguments);
        }

        if (_searched)
        {
            return false;
        }

        _searched = true;

        foreach ((string program, string arguments) in Candidates())
        {
            if (TryRun(program, arguments))
            {
                _known = (program, arguments);
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<(string Program, string Arguments)> Candidates()
    {
        if (OperatingSystem.IsMacOS())
        {
            yield return ("/usr/bin/afplay", "/System/Library/Sounds/Sosumi.aiff");
            yield return ("/usr/bin/afplay", "/System/Library/Sounds/Ping.aiff");
            yield break;
        }

        // Linux, in descending order of how likely the daemon is to be running.
        yield return ("canberra-gtk-play", "--id=dialog-warning");
        yield return ("paplay", "/usr/share/sounds/freedesktop/stereo/dialog-warning.oga");
        yield return ("aplay", "-q /usr/share/sounds/alsa/Front_Center.wav");
    }

    private static bool TryRun(string program, string arguments)
    {
        try
        {
            var start = new ProcessStartInfo(program, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };

            using Process? process = Process.Start(start);

            if (process is null)
            {
                return false;
            }

            // A player that has not failed within a second is playing. Waiting
            // for the sound to finish would block the interface thread for as
            // long as the tone lasts.
            if (process.WaitForExit(1000))
            {
                return process.ExitCode == 0;
            }

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Two short tones rather than one, because a single beep is what every
    /// other dialogue on the machine also makes.
    ///
    /// Marked as Windows-only rather than merely guarded by a runtime check:
    /// <c>Console.Beep(int, int)</c> throws on Linux and macOS, and the
    /// attribute is what lets the analyser confirm the call cannot be reached
    /// there. A caught exception would have hidden the same mistake at runtime.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static bool WindowsBeep()
    {
        try
        {
            Console.Beep(1046, 180);
            Console.Beep(784, 260);
            return true;
        }
        catch (Exception)
        {
            // No console and no sound device are both normal on a vehicle laptop.
            return false;
        }
    }
}
