using System.Security.Cryptography;

namespace ElwMeteo.Core.Updates;

public sealed class UpdateDownloadException(string message, Exception? inner = null)
    : Exception(message, inner);

/// <summary>
/// Fetches a release archive and checks that what arrived is what was promised.
///
/// The check is against the size and SHA-256 the GitHub API reported for the
/// asset. Be clear about what that buys: it catches a truncated or corrupted
/// download — the realistic failure on a cellular link — but it is not a
/// signature. Hash and file come from the same place, so the actual trust anchor
/// is that both were fetched from api.github.com over TLS.
/// </summary>
public sealed class UpdateDownloader(HttpClient httpClient)
{
    /// <summary>Progress as a fraction between 0 and 1, or null while unknown.</summary>
    public event Action<double?>? ProgressChanged;

    public async Task<string> DownloadAsync(
        ReleaseAsset asset,
        string targetDirectory,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(targetDirectory);

        // A stray path separator in the asset name must not write outside the
        // target directory.
        string fileName = Path.GetFileName(asset.Name);

        if (string.IsNullOrWhiteSpace(fileName) || !fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            throw new UpdateDownloadException($"Unerwarteter Dateiname im Release: „{asset.Name}“.");
        }

        string path = Path.Combine(targetDirectory, fileName);

        try
        {
            using HttpResponseMessage response = await httpClient
                .GetAsync(asset.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            long? expected = response.Content.Headers.ContentLength ?? (asset.Size > 0 ? asset.Size : null);

            await using (Stream source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var target = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[81920];
                long written = 0;
                int read;

                while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    written += read;

                    ProgressChanged?.Invoke(expected is > 0 ? Math.Min(1.0, (double)written / expected.Value) : null);
                }
            }

            Verify(path, asset);
            return path;
        }
        catch (UpdateDownloadException)
        {
            Discard(path);
            throw;
        }
        catch (OperationCanceledException)
        {
            Discard(path);
            throw;
        }
        catch (Exception ex)
        {
            Discard(path);
            throw new UpdateDownloadException($"Download fehlgeschlagen: {ex.Message}", ex);
        }
    }

    /// <summary>Throws unless the file matches the advertised size and hash.</summary>
    internal static void Verify(string path, ReleaseAsset asset)
    {
        var file = new FileInfo(path);

        if (asset.Size > 0 && file.Length != asset.Size)
        {
            throw new UpdateDownloadException(
                $"Die geladene Datei ist {file.Length} Byte groß, erwartet waren {asset.Size}. " +
                "Der Download ist unvollständig.");
        }

        if (asset.Sha256 is null)
        {
            // Older releases carry no digest. Say nothing here — the caller
            // reports it, so the operator knows the check was skipped.
            return;
        }

        string actual = ComputeSha256(path);

        if (!string.Equals(actual, asset.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new UpdateDownloadException(
                "Die Prüfsumme der geladenen Datei stimmt nicht mit der von GitHub gemeldeten überein. " +
                "Die Datei wurde verworfen.");
        }
    }

    internal static string ComputeSha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static void Discard(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover file in a temp folder is not worth a second failure.
        }
    }
}
