namespace ElwMeteo.Core.Meteorology;

/// <summary>WMO 4677 present-weather codes as delivered by Open-Meteo, in German.</summary>
public static class WeatherCodes
{
    private static readonly Dictionary<int, string> Descriptions = new()
    {
        [0] = "klar",
        [1] = "überwiegend klar",
        [2] = "teils wolkig",
        [3] = "bedeckt",
        [45] = "Nebel",
        [48] = "Nebel mit Reifansatz",
        [51] = "leichter Sprühregen",
        [53] = "Sprühregen",
        [55] = "starker Sprühregen",
        [56] = "leichter gefrierender Sprühregen",
        [57] = "gefrierender Sprühregen",
        [61] = "leichter Regen",
        [63] = "Regen",
        [65] = "starker Regen",
        [66] = "leichter gefrierender Regen",
        [67] = "gefrierender Regen",
        [71] = "leichter Schneefall",
        [73] = "Schneefall",
        [75] = "starker Schneefall",
        [77] = "Schneegriesel",
        [80] = "leichte Regenschauer",
        [81] = "Regenschauer",
        [82] = "heftige Regenschauer",
        [85] = "leichte Schneeschauer",
        [86] = "starke Schneeschauer",
        [95] = "Gewitter",
        [96] = "Gewitter mit leichtem Hagel",
        [99] = "Gewitter mit starkem Hagel"
    };

    public static string Describe(int? code) =>
        code is not null && Descriptions.TryGetValue(code.Value, out string? text)
            ? text
            : "keine Angabe";

    /// <summary>True for codes that warrant a note in the situation report on their own.</summary>
    public static bool IsSignificant(int? code) => code is 45 or 48 or 56 or 57 or 66 or 67 or 82 or 86 or 95 or 96 or 99;

    /// <summary>A single glyph for compact display next to the temperature.</summary>
    public static string Glyph(int? code, bool isDay) => code switch
    {
        0 or 1 => isDay ? "☀" : "☾",
        2 => "⛅",
        3 => "☁",
        45 or 48 => "≡",
        >= 51 and <= 57 => "☂",
        >= 61 and <= 67 => "☔",
        >= 71 and <= 77 => "❄",
        >= 80 and <= 82 => "🌧",
        85 or 86 => "🌨",
        >= 95 => "⚡",
        _ => "•"
    };
}
