using System.Globalization;
using System.Text.Json;
using ElwMeteo.Core.Models;

namespace ElwMeteo.Core.Services;

/// <summary>One selectable region of the NINA warning system.</summary>
public sealed record NinaRegion(string Ars, string Name)
{
    public override string ToString() => $"{Name} ({Ars})";
}

/// <summary>
/// Civil-protection warnings from NINA, the federal warning system run by the
/// BBK — the same source as the NINA app on a phone.
///
/// This is a different category from the DWD warnings the application already
/// shows, and that is the reason for having it: MoWaS, KATWARN and BIWAPP carry
/// hazardous-material releases, ordnance finds, drinking-water advisories and
/// evacuations, and LHP carries flood levels. On an operations vehicle those
/// are the messages that change what happens next, and none of them come out of
/// a weather feed.
///
/// The federal system indexes by Amtlicher Regionalschlüssel, not by
/// coordinates — there is no „warnings near this point" endpoint. So the region
/// is chosen once in the settings, the way the NINA app itself asks for it. For
/// a vehicle with a fixed area of operations that is the right unit anyway, and
/// it is honest about what is being queried instead of implying a precision the
/// interface does not have.
/// </summary>
public sealed class NinaWarningProvider(HttpClient httpClient) : IWarningProvider
{
    private const string BaseAddress = "https://warnung.bund.de/api31";

    /// <summary>Published list of regions and their keys, used by the search box.</summary>
    private const string RegionCatalogUrl = "https://warnung.bund.de/assets/json/suche_channel.json";

    /// <summary>
    /// Whether this source is queried at all. Off by default: it costs two
    /// requests per refresh and only makes sense once a region has been chosen.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>Regional key of the district or city to watch, twelve digits.</summary>
    public string Ars { get; set; } = string.Empty;

    /// <summary>Most recent region name, for the source line.</summary>
    public string RegionName { get; set; } = string.Empty;

    /// <summary>
    /// Sources to skip. The dashboard carries DWD weather warnings too, but the
    /// application already fetches those from Bright Sky and the DWD GeoServer,
    /// which resolve to the municipality boundary rather than the whole
    /// district. Taking them from here as well would list every storm warning
    /// twice, worded differently — and a duplicated warning is one somebody
    /// starts skimming past.
    /// </summary>
    public HashSet<string> ExcludedProviders { get; } =
        new(["DWD"], StringComparer.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<DwdWarning>> GetAsync(
        GeoPosition position,
        CancellationToken cancellationToken = default)
    {
        string ars = NormaliseArs(Ars);

        if (!Enabled || ars.Length == 0)
        {
            // Not configured is not an error: the operator simply has not picked
            // a region yet, and saying so beats an exception in the log.
            return [];
        }

        List<string> identifiers;

        try
        {
            using var response = await httpClient
                .GetAsync($"{BaseAddress}/dashboard/{ars}.json", cancellationToken)
                .ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            await using Stream stream = await response.Content
                .ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using JsonDocument document = await JsonDocument
                .ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

            identifiers = ReadDashboardIdentifiers(document.RootElement)
                .Where(entry => !ExcludedProviders.Contains(entry.Provider))
                .Select(entry => entry.Identifier)
                .ToList();
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            throw new WarningProviderException($"NINA-Warnungen nicht abrufbar: {ex.Message}", ex);
        }

        var warnings = new List<DwdWarning>();

        foreach (string identifier in identifiers)
        {
            DwdWarning? detail = await LoadDetailAsync(identifier, cancellationToken).ConfigureAwait(false);

            if (detail is not null)
            {
                warnings.Add(detail);
            }
        }

        return warnings
            .OrderByDescending(w => w.Level)
            .ThenBy(w => w.Start ?? DateTimeOffset.MaxValue)
            .ToList();
    }

    /// <summary>
    /// A single warning's detail. One unreadable message must not take the whole
    /// set with it — the other four might be the important ones.
    /// </summary>
    private async Task<DwdWarning?> LoadDetailAsync(string identifier, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient
                .GetAsync($"{BaseAddress}/warnings/{Uri.EscapeDataString(identifier)}.json", cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using Stream stream = await response.Content
                .ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using JsonDocument document = await JsonDocument
                .ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

            return ParseWarning(document.RootElement);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            return null;
        }
    }

    // ------------------------------------------------------------- parsing

    /// <summary>One entry of the dashboard, before its detail is fetched.</summary>
    internal sealed record DashboardEntry(string Identifier, string Provider);

    /// <summary>
    /// Identifiers from a dashboard response. Cancellations are dropped here
    /// rather than fetched and discarded later: a withdrawn message must not
    /// show up as an active warning.
    /// </summary>
    internal static List<DashboardEntry> ReadDashboardIdentifiers(JsonElement root)
    {
        var entries = new List<DashboardEntry>();

        if (root.ValueKind != JsonValueKind.Array)
        {
            return entries;
        }

        foreach (JsonElement item in root.EnumerateArray())
        {
            string? id = ReadString(item, "id");

            if (id is null)
            {
                continue;
            }

            string provider = string.Empty;

            if (item.TryGetProperty("payload", out JsonElement payload) &&
                payload.TryGetProperty("data", out JsonElement data))
            {
                if (string.Equals(ReadString(data, "msgType"), "Cancel", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                provider = ReadString(data, "provider") ?? string.Empty;
            }

            entries.Add(new DashboardEntry(id, provider));
        }

        return entries;
    }

    /// <summary>Turns one CAP-shaped NINA message into the application's warning type.</summary>
    internal static DwdWarning? ParseWarning(JsonElement root)
    {
        if (string.Equals(ReadString(root, "msgType"), "Cancel", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!root.TryGetProperty("info", out JsonElement infos) ||
            infos.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        // Several language variants may be present; German first, else the first
        // one that is there at all.
        JsonElement info = PickGermanInfo(infos);

        if (info.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        string? eventName = ReadString(info, "event");
        string headline = ReadString(info, "headline") ?? eventName ?? "Warnmeldung";
        WarningLevel level = ParseSeverity(ReadString(info, "severity"));

        // Exercises and system tests are shown but held below the alert
        // threshold. Hiding them would be confusing on a nationwide Warntag;
        // letting them sound the alarm would be worse.
        string status = ReadString(root, "status") ?? "Actual";
        bool isReal = string.Equals(status, "Actual", StringComparison.OrdinalIgnoreCase);

        if (!isReal)
        {
            eventName = $"PROBE — {eventName ?? headline}";
            level = WarningLevel.Minor;
        }

        return new DwdWarning(
            Event: eventName ?? headline,
            Headline: headline,
            Level: level,
            Start: ReadTime(info, "onset", "effective") ?? ReadTime(root, "sent"),
            End: ReadTime(info, "expires"),
            RegionName: ReadAreaDescription(info) ?? ReadString(info, "senderName") ?? "—",
            Description: ReadString(info, "description"),
            Instruction: ReadString(info, "instruction"));
    }

    private static JsonElement PickGermanInfo(JsonElement infos)
    {
        JsonElement first = default;
        bool haveFirst = false;

        foreach (JsonElement info in infos.EnumerateArray())
        {
            if (!haveFirst)
            {
                first = info;
                haveFirst = true;
            }

            string? language = ReadString(info, "language");

            if (language is not null && language.StartsWith("de", StringComparison.OrdinalIgnoreCase))
            {
                return info;
            }
        }

        return haveFirst ? first : default;
    }

    private static string? ReadAreaDescription(JsonElement info)
    {
        if (!info.TryGetProperty("area", out JsonElement areas) || areas.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var names = areas.EnumerateArray()
            .Select(a => ReadString(a, "areaDesc"))
            .Where(n => n is not null)
            .Take(3)
            .ToList();

        return names.Count == 0 ? null : string.Join(", ", names);
    }

    private static WarningLevel ParseSeverity(string? severity) => severity?.Trim().ToLowerInvariant() switch
    {
        "minor" => WarningLevel.Minor,
        "moderate" => WarningLevel.Moderate,
        "severe" => WarningLevel.Severe,
        "extreme" => WarningLevel.Extreme,
        // "Unknown" is common in MoWaS messages and must not read as harmless.
        _ => WarningLevel.Moderate
    };

    // --------------------------------------------------------- region list

    /// <summary>
    /// Downloads the list of regions so the operator can search for their
    /// district by name instead of looking up a twelve-digit key.
    /// </summary>
    public async Task<IReadOnlyList<NinaRegion>> GetRegionsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await httpClient.GetAsync(RegionCatalogUrl, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using Stream stream = await response.Content
                .ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using JsonDocument document = await JsonDocument
                .ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

            return ParseRegions(document.RootElement);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            throw new WarningProviderException($"Regionsliste nicht abrufbar: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Reads the published region list. Two shapes are accepted because the file
    /// has changed form before and a rename of one field should not cost the
    /// whole search box.
    /// </summary>
    internal static List<NinaRegion> ParseRegions(JsonElement root)
    {
        var regions = new List<NinaRegion>();

        // { "daten": [ ["055150000000", "Essen, Stadt"], ... ] }
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("daten", out JsonElement rows) &&
            rows.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement row in rows.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                var pair = row.EnumerateArray().Take(2).ToList();

                if (pair.Count == 2 &&
                    pair[0].ValueKind == JsonValueKind.String &&
                    pair[1].ValueKind == JsonValueKind.String)
                {
                    Add(regions, pair[0].GetString(), pair[1].GetString());
                }
            }

            return regions;
        }

        // [ { "code": "...", "name": "..." }, ... ]
        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in root.EnumerateArray())
            {
                Add(regions,
                    ReadString(item, "code") ?? ReadString(item, "key") ?? ReadString(item, "ars"),
                    ReadString(item, "name") ?? ReadString(item, "label"));
            }
        }

        return regions;

        static void Add(List<NinaRegion> into, string? key, string? name)
        {
            string normalised = NormaliseArs(key);

            if (normalised.Length > 0 && !string.IsNullOrWhiteSpace(name))
            {
                into.Add(new NinaRegion(normalised, name.Trim()));
            }
        }
    }

    /// <summary>Regions whose name contains every word of the query, best match first.</summary>
    public static IReadOnlyList<NinaRegion> Search(IEnumerable<NinaRegion> regions, string query, int limit = 25)
    {
        string[] words = (query ?? string.Empty)
            .Split([' ', ',', '-'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (words.Length == 0)
        {
            return [];
        }

        return regions
            .Where(r => words.All(w => r.Name.Contains(w, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(r => r.Name.Length)
            .ThenBy(r => r.Name, StringComparer.CurrentCulture)
            .Take(limit)
            .ToList();
    }

    /// <summary>
    /// Brings a regional key into the form the dashboard endpoint expects:
    /// twelve digits, and everything below the district zeroed.
    ///
    /// The published list carries municipality-level keys, but the dashboard is
    /// only served per district or independent city. Passing a municipality key
    /// straight through answers 404, which would look like „no warnings" —
    /// exactly the failure this whole feature is meant to prevent.
    /// </summary>
    internal static string NormaliseArs(string? value)
    {
        string digits = new((value ?? string.Empty).Where(char.IsAsciiDigit).ToArray());

        if (digits.Length < 5)
        {
            return string.Empty;
        }

        return digits[..5] + "0000000";
    }

    // -------------------------------------------------------------- helpers

    private static string? ReadString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out JsonElement value) &&
        value.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!.Trim()
            : null;

    private static DateTimeOffset? ReadTime(JsonElement element, params string[] names)
    {
        foreach (string name in names)
        {
            string? raw = ReadString(element, name);

            if (raw is not null &&
                DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTimeOffset parsed))
            {
                return parsed;
            }
        }

        return null;
    }
}
