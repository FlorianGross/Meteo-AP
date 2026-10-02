namespace ElwMeteo.Core.Kiosk;

/// <summary>
/// What has to be written so the application comes up by itself after a reboot.
///
/// Three systems, three mechanisms, and all three are plain text or a registry
/// string — so the content is generated here, in the domain library, and only
/// the writing happens in the heads. The same reasoning as the update swap
/// scripts: a generator whose output can be asserted on is one whose quoting
/// bugs are found by a test rather than by a vehicle that does not come up.
///
/// Quoting is the whole risk. The default installation path contains a space
/// (`…\Programs\ELW-Meteo\…`) and so does every user profile with a space in
/// the account name, which is most of them.
/// </summary>
public static class AutostartEntry
{
    /// <summary>Value name under the Run key, and the base name of the files.</summary>
    public const string Name = "ELW-Meteo";

    // ------------------------------------------------------------- Windows

    /// <summary>
    /// The command line for <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>.
    ///
    /// Always quoted, even when the path has no space in it: the Run key splits
    /// on whitespace, and an unquoted `C:\Program Files\…` starts
    /// `C:\Program` with the rest as arguments. Quoting unconditionally means
    /// the one path that needed it is never the one that was missed.
    /// </summary>
    public static string BuildWindowsCommand(string executablePath) =>
        $"\"{executablePath.Trim('"')}\"";

    // --------------------------------------------------------------- Linux

    /// <summary>Freedesktop autostart file name.</summary>
    public const string DesktopFileName = "elw-meteo.desktop";

    /// <summary>
    /// A freedesktop.org autostart entry. Goes into
    /// <c>~/.config/autostart/</c>, which every mainstream desktop reads.
    /// </summary>
    public static string BuildDesktopEntry(string executablePath) =>
        string.Join('\n',
        [
            "[Desktop Entry]",
            "Type=Application",
            $"Name={Name}",
            "Comment=Wetter- und Lageübersicht für den Einsatzleitwagen",
            // Exec takes a command line, so the same quoting rule as Windows.
            $"Exec=\"{executablePath.Trim('"')}\"",
            "Terminal=false",
            "X-GNOME-Autostart-enabled=true",
            ""
        ]);

    // --------------------------------------------------------------- macOS

    public const string LaunchAgentFileName = "de.elw-meteo.autostart.plist";

    /// <summary>
    /// A LaunchAgent for <c>~/Library/LaunchAgents/</c>.
    ///
    /// <c>RunAtLoad</c> without <c>KeepAlive</c> on purpose: the application
    /// should start with the session, but somebody who closes it means to close
    /// it. A window that reappears because launchd restarted it is the kind of
    /// behaviour that gets software banned from a vehicle.
    /// </summary>
    public static string BuildLaunchAgent(string executablePath) =>
        $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
        <plist version="1.0">
        <dict>
          <key>Label</key>
          <string>de.elw-meteo.autostart</string>
          <key>ProgramArguments</key>
          <array>
            <string>{EscapeXml(executablePath)}</string>
          </array>
          <key>RunAtLoad</key>
          <true/>
        </dict>
        </plist>

        """;

    /// <summary>
    /// Escapes a path for an XML text node. A path can legally contain an
    /// ampersand, and an unescaped one makes the plist unparseable — at which
    /// point launchd ignores the file silently and autostart simply never
    /// happens, with nothing anywhere saying why.
    /// </summary>
    internal static string EscapeXml(string value) => value
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal);
}
