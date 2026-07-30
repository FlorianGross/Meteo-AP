using System.Text.Json;

namespace ElwMeteo.Core.Updates;

public sealed class UpdateCheckException(string message, Exception? inner = null)
    : Exception(message, inner);

/// <summary>
/// Reads the releases of a public GitHub repository.
///
/// Only the public, unauthenticated API is used — no token, nothing to store on
/// a vehicle laptop. That costs a rate limit of sixty requests per hour per
/// address, which is why the caller remembers when it last looked instead of
/// asking on every start.
/// </summary>
public sealed class GitHubReleaseProvider(HttpClient httpClient)
{
    /// <summary>owner/name of the repository the packages come from.</summary>
    public const string DefaultRepository = "FlorianGross/ELW-Meteo";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// The newest release that carries a usable version, or null when the
    /// repository has none. Pre-releases are only considered when asked for.
    /// </summary>
    public async Task<ReleaseInfo?> GetLatestAsync(
        string repository,
        bool includePreReleases = false,
        CancellationToken cancellationToken = default)
    {
        if (!IsPlausibleRepository(repository))
        {
            throw new UpdateCheckException(
                $"„{repository}“ ist keine gültige Angabe. Erwartet wird Eigentümer/Name, z. B. {DefaultRepository}.");
        }

        // The list endpoint rather than /latest: /latest hides pre-releases
        // entirely and would make the setting impossible to honour.
        string url = $"https://api.github.com/repos/{repository}/releases?per_page=20";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

            using HttpResponseMessage response = await httpClient
                .SendAsync(request, cancellationToken)
                .ConfigureAwait(false);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                throw new UpdateCheckException(
                    $"Das Repository „{repository}“ ist nicht erreichbar oder nicht öffentlich.");
            }

            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                // GitHub answers 403 for an exhausted rate limit, not 429.
                throw new UpdateCheckException(
                    "GitHub hat die Anfrage abgewiesen — sehr wahrscheinlich ist das Anfragelimit " +
                    "erschöpft (60 Anfragen je Stunde ohne Anmeldung). Später erneut versuchen.");
            }

            response.EnsureSuccessStatusCode();

            await using Stream stream = await response.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);

            using JsonDocument document = await JsonDocument
                .ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return Newest(Parse(document.RootElement), includePreReleases);
        }
        catch (UpdateCheckException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            throw new UpdateCheckException(
                $"Aktualisierungsprüfung fehlgeschlagen: {Innermost(ex).Message}", ex);
        }
    }

    /// <summary>Picks the highest version, honouring the pre-release setting.</summary>
    internal static ReleaseInfo? Newest(IEnumerable<ReleaseInfo> releases, bool includePreReleases)
    {
        ReleaseInfo? best = null;

        foreach (ReleaseInfo release in releases)
        {
            if (release.IsPreRelease && !includePreReleases)
            {
                continue;
            }

            if (best is null || release.Version > best.Version)
            {
                best = release;
            }
        }

        return best;
    }

    /// <summary>
    /// Turns the API's array into releases, skipping drafts and anything whose
    /// tag is not a version — a tag like "nightly" cannot be compared.
    /// </summary>
    internal static List<ReleaseInfo> Parse(JsonElement root)
    {
        var releases = new List<ReleaseInfo>();

        if (root.ValueKind != JsonValueKind.Array)
        {
            return releases;
        }

        foreach (JsonElement element in root.EnumerateArray())
        {
            if (element.TryGetProperty("draft", out JsonElement draft) &&
                draft.ValueKind == JsonValueKind.True)
            {
                continue;
            }

            string? tag = Text(element, "tag_name");

            if (!AppVersion.TryParse(tag, out AppVersion version))
            {
                continue;
            }

            releases.Add(new ReleaseInfo
            {
                Version = version,
                TagName = tag!,
                Title = Text(element, "name") ?? tag!,
                Notes = Text(element, "body") ?? string.Empty,
                PublishedAt = element.TryGetProperty("published_at", out JsonElement published) &&
                              published.ValueKind == JsonValueKind.String &&
                              DateTimeOffset.TryParse(published.GetString(), out DateTimeOffset stamp)
                    ? stamp
                    : null,
                IsPreRelease = element.TryGetProperty("prerelease", out JsonElement pre) &&
                               pre.ValueKind == JsonValueKind.True,
                HtmlUrl = Text(element, "html_url") ?? string.Empty,
                Assets = ParseAssets(element)
            });
        }

        return releases;
    }

    private static List<ReleaseAsset> ParseAssets(JsonElement release)
    {
        var assets = new List<ReleaseAsset>();

        if (!release.TryGetProperty("assets", out JsonElement array) ||
            array.ValueKind != JsonValueKind.Array)
        {
            return assets;
        }

        foreach (JsonElement element in array.EnumerateArray())
        {
            string? name = Text(element, "name");
            string? url = Text(element, "browser_download_url");

            if (name is null || url is null)
            {
                continue;
            }

            // Only ever download over HTTPS, whatever the API returned.
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? parsed) ||
                parsed.Scheme != Uri.UriSchemeHttps)
            {
                continue;
            }

            assets.Add(new ReleaseAsset
            {
                Name = name,
                DownloadUrl = url,
                Size = element.TryGetProperty("size", out JsonElement size) &&
                       size.ValueKind == JsonValueKind.Number
                    ? size.GetInt64()
                    : 0,
                Sha256 = StripDigestPrefix(Text(element, "digest"))
            });
        }

        return assets;
    }

    /// <summary>GitHub reports "sha256:abc…"; the checker wants the hex only.</summary>
    internal static string? StripDigestPrefix(string? digest)
    {
        if (string.IsNullOrWhiteSpace(digest))
        {
            return null;
        }

        const string prefix = "sha256:";

        return digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? digest[prefix.Length..].Trim()
            : null;
    }

    /// <summary>Rejects anything that is not a plain owner/name pair.</summary>
    internal static bool IsPlausibleRepository(string? repository)
    {
        if (string.IsNullOrWhiteSpace(repository))
        {
            return false;
        }

        string[] parts = repository.Split('/');

        return parts.Length == 2 &&
               parts.All(part =>
                   part.Length > 0 &&
                   part.Length <= 100 &&
                   part.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'));
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static Exception Innermost(Exception exception)
    {
        Exception current = exception;

        while (current.InnerException is not null)
        {
            current = current.InnerException;
        }

        return current;
    }
}
