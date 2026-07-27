namespace ElwMeteo.Core.Models;

/// <summary>DWD warning severity, ordered so that higher means worse.</summary>
public enum WarningLevel
{
    None = 0,
    /// <summary>Stufe 1 — Wetterwarnung (gelb).</summary>
    Minor = 1,
    /// <summary>Stufe 2 — Warnung vor markantem Wetter (orange).</summary>
    Moderate = 2,
    /// <summary>Stufe 3 — Unwetterwarnung (rot).</summary>
    Severe = 3,
    /// <summary>Stufe 4 — Warnung vor extremem Unwetter (dunkelrot).</summary>
    Extreme = 4
}

public sealed record DwdWarning(
    string Event,
    string Headline,
    WarningLevel Level,
    DateTimeOffset? Start,
    DateTimeOffset? End,
    string RegionName,
    string? Description,
    string? Instruction)
{
    public bool IsActiveAt(DateTimeOffset instant) =>
        (Start is null || instant >= Start) && (End is null || instant <= End);

    public string LevelLabel => Level switch
    {
        WarningLevel.Minor => "Stufe 1 · Wetterwarnung",
        WarningLevel.Moderate => "Stufe 2 · markantes Wetter",
        WarningLevel.Severe => "Stufe 3 · Unwetter",
        WarningLevel.Extreme => "Stufe 4 · extremes Unwetter",
        _ => "keine Warnung"
    };

    /// <summary>Hex colour matching the DWD warning colours.</summary>
    public string LevelColour => Level switch
    {
        WarningLevel.Minor => "#FFD400",
        WarningLevel.Moderate => "#FF8C00",
        WarningLevel.Severe => "#E1002F",
        WarningLevel.Extreme => "#8B0000",
        _ => "#4A5568"
    };

    public string TimeRangeLabel
    {
        get
        {
            string from = Start?.ToLocalTime().ToString("dd.MM. HH:mm") ?? "—";
            string to = End?.ToLocalTime().ToString("dd.MM. HH:mm") ?? "offen";
            return $"{from} – {to}";
        }
    }
}
