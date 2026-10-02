namespace ElwMeteo.Core.Kiosk;

/// <summary>
/// A view the unattended rotation can stop on.
///
/// Identified by a string, not by the tab index, because the index is a property
/// of the XAML and the id is what ends up in the settings file. Inserting a tab
/// one day must not silently turn a saved rotation of "clock, weather, radar"
/// into "clock, weather, diagnostics".
/// </summary>
public sealed record KioskStation(string Id, string Title, int TabIndex);

/// <summary>
/// The stations offered for rotation, in the order the tabs appear.
///
/// Diagnostics and settings are deliberately absent. The rotation is what a
/// screen in a vehicle shows when nobody is operating it, and a kiosk that
/// parks itself on the settings page — with every API key and serial port on
/// display, within reach of whoever walks past — is worse than one tab that
/// never gets shown automatically. Both remain reachable by hand.
/// </summary>
public static class KioskStationCatalog
{
    public const string Clock = "uhr";
    public const string Situation = "lage";
    public const string Map = "karte";
    public const string Web = "web";
    public const string Trend = "verlauf";

    public static IReadOnlyList<KioskStation> All { get; } =
    [
        new KioskStation(Clock, "Uhr", 0),
        new KioskStation(Situation, "Lage & Wetter", 1),
        new KioskStation(Map, "Karten & Radar", 2),
        new KioskStation(Web, "Web-Radar", 3),
        new KioskStation(Trend, "Verlauf", 4)
    ];

    /// <summary>What a fresh installation rotates through: the situation, the radar, the clock.</summary>
    public static IReadOnlyList<string> DefaultRotation { get; } = [Situation, Map, Clock];

    public static KioskStation? ById(string? id) =>
        All.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));

    public static KioskStation? ByTabIndex(int tabIndex) =>
        All.FirstOrDefault(s => s.TabIndex == tabIndex);

    /// <summary>
    /// Turns stored ids into stations, dropping anything unknown and anything
    /// listed twice. An id that no longer exists must not stall the rotation on
    /// a station that cannot be shown.
    /// </summary>
    public static IReadOnlyList<KioskStation> Resolve(IEnumerable<string>? ids)
    {
        if (ids is null)
        {
            return [];
        }

        List<KioskStation> resolved = [];

        foreach (string id in ids)
        {
            KioskStation? station = ById(id);

            if (station is not null && !resolved.Any(s => s.Id == station.Id))
            {
                resolved.Add(station);
            }
        }

        return resolved;
    }
}
