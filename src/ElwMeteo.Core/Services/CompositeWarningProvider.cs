using ElwMeteo.Core.Models;

namespace ElwMeteo.Core.Services;

/// <summary>
/// Fetches DWD warnings from Bright Sky's CAP feed — the same source the
/// WarnWetter app uses — and falls back to the DWD GeoServer when it is
/// unreachable.
///
/// Two independent paths to the same official data matter here: a warning that
/// silently fails to appear is worse than one that arrives from the slower route.
/// </summary>
public sealed class CompositeWarningProvider(
    BrightSkyProvider brightSky,
    DwdWarningProvider geoServer) : IWarningProvider
{
    /// <summary>Which route produced the warnings that are currently shown.</summary>
    public string LastSourceLabel { get; private set; } = "—";

    public async Task<IReadOnlyList<DwdWarning>> GetAsync(
        GeoPosition position,
        CancellationToken cancellationToken = default)
    {
        try
        {
            IReadOnlyList<DwdWarning> alerts = await brightSky
                .GetAlertsAsync(position, cancellationToken)
                .ConfigureAwait(false);

            LastSourceLabel = "DWD CAP über Bright Sky";
            return alerts;
        }
        catch (WarningProviderException)
        {
            // Fall through to the GeoServer rather than reporting "no warnings".
        }

        try
        {
            IReadOnlyList<DwdWarning> features = await geoServer
                .GetAsync(position, cancellationToken)
                .ConfigureAwait(false);

            LastSourceLabel = "DWD GeoServer (Ausweichquelle)";
            return features;
        }
        catch (WarningProviderException)
        {
            LastSourceLabel = "keine Quelle erreichbar";
            throw;
        }
    }
}
