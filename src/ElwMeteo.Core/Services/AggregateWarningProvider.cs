using ElwMeteo.Core.Models;

namespace ElwMeteo.Core.Services;

/// <summary>
/// Puts the weather warnings and the civil-protection warnings into one list.
///
/// The two are fetched independently and neither is allowed to suppress the
/// other. If NINA is unreachable the storm warning still arrives; if the DWD
/// route is down the hazardous-material message still arrives. Only when every
/// route has failed does this report a failure — and then it says which ones,
/// because „no warnings" and „could not ask" look identical on a panel and mean
/// opposite things.
/// </summary>
public sealed class AggregateWarningProvider(params IWarningProvider[] providers) : IWarningProvider
{
    private readonly IReadOnlyList<IWarningProvider> _providers = providers;

    /// <summary>Which routes answered, for the source line under the warning list.</summary>
    public string LastSourceLabel { get; private set; } = "—";

    /// <summary>Routes that failed on the last attempt, named so they can be checked.</summary>
    public IReadOnlyList<string> LastFailures { get; private set; } = [];

    public async Task<IReadOnlyList<DwdWarning>> GetAsync(
        GeoPosition position,
        CancellationToken cancellationToken = default)
    {
        var collected = new List<DwdWarning>();
        var sources = new List<string>();
        var failures = new List<string>();

        foreach (IWarningProvider provider in _providers)
        {
            try
            {
                IReadOnlyList<DwdWarning> warnings = await provider
                    .GetAsync(position, cancellationToken)
                    .ConfigureAwait(false);

                collected.AddRange(warnings);
                sources.Add(Describe(provider));
            }
            catch (WarningProviderException ex)
            {
                failures.Add($"{Describe(provider)}: {ex.Message}");
            }
        }

        LastFailures = failures;

        if (sources.Count == 0)
        {
            LastSourceLabel = "keine Quelle erreichbar";

            throw new WarningProviderException(failures.Count > 0
                ? string.Join(" · ", failures)
                : "Keine Warnquelle konfiguriert.");
        }

        LastSourceLabel = failures.Count == 0
            ? string.Join(" + ", sources)
            : $"{string.Join(" + ", sources)} — nicht erreichbar: {failures.Count}";

        return collected
            .OrderByDescending(w => w.Level)
            .ThenBy(w => w.Start ?? DateTimeOffset.MaxValue)
            .ToList();
    }

    private static string Describe(IWarningProvider provider) => provider switch
    {
        CompositeWarningProvider composite => composite.LastSourceLabel,
        NinaWarningProvider nina => string.IsNullOrWhiteSpace(nina.RegionName)
            ? "NINA (Bevölkerungsschutz)"
            : $"NINA {nina.RegionName}",
        _ => provider.GetType().Name
    };
}
