using System.Globalization;

namespace ElwMeteo.Core.Maps;

/// <summary>
/// A web page shown inside the application's browser view.
///
/// The URL may contain <c>{lat}</c>, <c>{lon}</c> and <c>{zoom}</c>; those are
/// substituted with the incident position, so a viewer opens where the vehicle
/// is instead of on a country overview.
/// </summary>
public sealed record WebViewSource
{
    public required string Id { get; init; }

    public required string Title { get; init; }

    public required string UrlTemplate { get; init; }

    public string Group { get; init; } = "Radar";

    public string? Description { get; init; }

    /// <summary>False for entries the operator added, true for the shipped ones.</summary>
    public bool IsBuiltIn { get; init; }

    public bool FollowsPosition =>
        UrlTemplate.Contains("{lat}", StringComparison.Ordinal) ||
        UrlTemplate.Contains("{lon}", StringComparison.Ordinal);

    /// <summary>
    /// Fills in the position placeholders. Coordinates use the invariant culture:
    /// a German decimal comma in a URL would silently break the query.
    /// </summary>
    public string Resolve(double latitude, double longitude, int zoom)
    {
        return UrlTemplate
            .Replace("{lat}", latitude.ToString("F5", CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{lon}", longitude.ToString("F5", CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{zoom}", zoom.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether a template is safe to navigate to. Only http and https are
    /// allowed: a <c>file:</c> or <c>javascript:</c> URL in a settings file must
    /// never be opened just because it was typed into a text box.
    /// </summary>
    public static bool IsAcceptableUrl(string? urlTemplate)
    {
        if (string.IsNullOrWhiteSpace(urlTemplate))
        {
            return false;
        }

        // Placeholders are not valid URL characters everywhere, so test the
        // resolved form rather than the template.
        string probe = urlTemplate
            .Replace("{lat}", "50.0", StringComparison.Ordinal)
            .Replace("{lon}", "8.0", StringComparison.Ordinal)
            .Replace("{zoom}", "10", StringComparison.Ordinal);

        return Uri.TryCreate(probe, UriKind.Absolute, out Uri? uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }
}

/// <summary>
/// The web viewers the application offers out of the box.
///
/// These are the public pages of each provider, opened as a normal top-level
/// navigation — the same thing as visiting them in a browser. That matters
/// technically as well as legally: many of these sites refuse to be put in an
/// iframe, but navigating to them directly works, and it keeps their own
/// branding, attribution and terms in view.
///
/// The point of having them here at all: when an API is down, renamed or simply
/// wrong, the provider's own viewer is still a working second opinion.
/// </summary>
public static class WebViewSourceCatalog
{
    public static IReadOnlyList<WebViewSource> BuiltIn { get; } =
    [
        new WebViewSource
        {
            Id = "rainviewer-web",
            Title = "RainViewer",
            Group = "Regenradar",
            UrlTemplate = "https://www.rainviewer.com/map.html?loc={lat},{lon},{zoom}&oFa=0&oC=0&oU=0&oCS=1&oF=0&oAP=1&c=3&o=83&lm=1&layer=radar&sm=1&sn=1",
            Description = "Weltweites Radar mit Zeitleiste und Nowcast — dieselbe Quelle wie auf der Kartenregisterkarte, hier als vollständige Bedienoberfläche.",
            IsBuiltIn = true
        },
        new WebViewSource
        {
            Id = "windy-radar",
            Title = "Windy — Radar",
            Group = "Regenradar",
            UrlTemplate = "https://www.windy.com/-Radar-radar?radar,{lat},{lon},{zoom}",
            Description = "Radar mit Animation, dazu Wind, Böen und Gewitter in derselben Ansicht.",
            IsBuiltIn = true
        },
        new WebViewSource
        {
            Id = "ventusky-radar",
            Title = "Ventusky — Niederschlag",
            Group = "Regenradar",
            UrlTemplate = "https://www.ventusky.com/?p={lat};{lon};{zoom}&l=radar",
            Description = "Niederschlagsradar mit Zeitachse. Ventusky bietet keine API an — als eingebettete Ansicht aber uneingeschränkt nutzbar.",
            IsBuiltIn = true
        },
        new WebViewSource
        {
            Id = "kachelmann-radar",
            Title = "Kachelmannwetter — Regenradar",
            Group = "Regenradar",
            UrlTemplate = "https://kachelmannwetter.com/de/regenradar/deutschland",
            Description = "Hoch aufgelöstes Radar für Deutschland mit eigener Nachbearbeitung.",
            IsBuiltIn = true
        },
        new WebViewSource
        {
            Id = "dwd-warnlage",
            Title = "DWD — Warnlage Deutschland",
            Group = "Amtlich (DWD)",
            UrlTemplate = "https://www.dwd.de/DE/wetter/warnungen_gemeinden/warnWetter_node.html",
            Description = "Amtliche Warnkarte des Deutschen Wetterdienstes auf Gemeindeebene.",
            IsBuiltIn = true
        },
        new WebViewSource
        {
            Id = "dwd-radarfilm",
            Title = "DWD — Niederschlagsradar",
            Group = "Amtlich (DWD)",
            UrlTemplate = "https://www.dwd.de/DE/leistungen/radarbild_film/radarbild_film.html",
            Description = "Amtliches Radarbild und Radarfilm des DWD.",
            IsBuiltIn = true
        },
        new WebViewSource
        {
            Id = "nina",
            Title = "NINA — Bevölkerungswarnungen",
            Group = "Amtlich (DWD)",
            UrlTemplate = "https://warnung.bund.de/karte",
            Description = "Warnungen des Bundes: Gefahrstoff, Ausfälle, Bevölkerungsschutz — nicht nur Wetter.",
            IsBuiltIn = true
        },
        new WebViewSource
        {
            Id = "blitzortung",
            Title = "Blitzortung",
            Group = "Gewitter",
            UrlTemplate = "https://map.blitzortung.org/#{zoom}/{lat}/{lon}",
            Description = "Blitzeinschläge in Echtzeit — zeigt die Zugbahn einer Gewitterzelle oft früher als das Radar.",
            IsBuiltIn = true
        },
        new WebViewSource
        {
            Id = "meteoblue",
            Title = "meteoblue — Niederschlagskarte",
            Group = "Modelle",
            // The path segment is "maps", not "karten": only "weather" is
            // translated in the German URL. The earlier /de/wetter/karten/index
            // pointed at a page that does not exist, which is why this entry
            // never worked. This is the fixed Germany map — verified to exist,
            // unlike the coordinate-following variant below.
            UrlTemplate = "https://www.meteoblue.com/de/wetter/karte/niederschlag/germany",
            Description = "Modellkarte für Niederschlag über Deutschland. Folgt der Einsatzstelle nicht — dafür gibt es „meteoblue (Position)“.",
            IsBuiltIn = true
        },
        new WebViewSource
        {
            Id = "meteoblue-coords",
            Title = "meteoblue — Karten (Position)",
            Group = "Modelle",
            // Fragment order is zoom/lat/lon, and the second slot of "map" is the
            // colour ramp, not an interval — "hourly" there was wrong as well.
            UrlTemplate = "https://www.meteoblue.com/de/wetter/maps/index#coords={zoom}/{lat}/{lon}&map=precipitation~rainbow~auto~sfc~none",
            Description = "Öffnet die Kartenseite an der Einsatzstelle. Springt die Karte nicht dorthin, ist die Deutschlandkarte darüber der verlässliche Weg.",
            IsBuiltIn = true
        },
        new WebViewSource
        {
            Id = "windy-wind",
            Title = "Windy — Wind und Böen",
            Group = "Modelle",
            UrlTemplate = "https://www.windy.com/-Wind-gust?gust,{lat},{lon},{zoom}",
            Description = "Windfeld und Böenprognose als Strömungsbild.",
            IsBuiltIn = true
        }
    ];

    public static WebViewSource? ById(string? id) =>
        BuiltIn.FirstOrDefault(s => s.Id == id);
}
