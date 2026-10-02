namespace ElwMeteo.Core.Maps;

/// <summary>
/// Decides which requests the embedded browser refuses to make.
///
/// Three reasons, in the order they matter on a vehicle:
///
/// The link is metered and often weak. An advertising network pulls scripts,
/// video and tracking pixels from a dozen hosts, and on a cellular connection in
/// a basement that is the difference between a radar picture arriving and not.
///
/// Second, the screen. A banner that pushes the radar below the fold is worse
/// here than it is on a desk.
///
/// Third, the providers' pages carry third-party trackers that have nothing to
/// do with the weather, and an operations vehicle is not a place to be
/// broadcasting a position trail to an ad exchange.
///
/// What this deliberately does NOT do:
///
///   · it never blocks a weather provider's own domain — the point is to keep
///     their page working, not to strip it
///   · it never blocks a consent manager. Those dialogues are annoying, and
///     blocking one leaves the page permanently grey behind an overlay that can
///     no longer be dismissed — a worse outcome than the dialogue
///   · it is a fixed list of known advertising and analytics networks, not a
///     pattern match. A wildcard would eventually swallow a tile server
///
/// The list is deliberately short and boring. Every entry is a network whose
/// sole business is advertising or measurement.
/// </summary>
public static class WebContentFilter
{
    /// <summary>
    /// Hosts that are refused. Matched on the registrable domain or any
    /// subdomain of it, never as a substring: "adform.net" must not also match
    /// "radform.net.example.com".
    /// </summary>
    public static IReadOnlyList<string> BlockedDomains { get; } =
    [
        // Google's advertising stack. Note that neither google.com nor the map
        // and font hosts are here — several provider pages need those.
        "doubleclick.net",
        "googlesyndication.com",
        "googleadservices.com",
        "googletagservices.com",
        "google-analytics.com",
        "googletagmanager.com",
        "adservice.google.com",
        "adservice.google.de",

        // Exchanges and demand-side platforms.
        "adnxs.com",
        "adform.net",
        "adition.com",
        "amazon-adsystem.com",
        "casalemedia.com",
        "criteo.com",
        "criteo.net",
        "openx.net",
        "pubmatic.com",
        "rubiconproject.com",
        "smartadserver.com",
        "yieldlab.net",
        "33across.com",
        "sharethrough.com",
        "teads.tv",

        // Content recommendation — the "you may also like" strips.
        "outbrain.com",
        "taboola.com",

        // Reach measurement and behaviour analytics.
        "scorecardresearch.com",
        "quantserve.com",
        "chartbeat.com",
        "hotjar.com",
        "mixpanel.com",
        "fullstory.com",
        "clarity.ms",

        // German reach measurement (IVW/AGOF).
        "ioam.de",
        "iocnt.net",
        "meetrics.net",

        // Social trackers. The networks themselves are not blocked, only the
        // measurement endpoints' hosts.
        "connect.facebook.net",
        "analytics.twitter.com",
        "static.ads-twitter.com"
    ];

    /// <summary>
    /// True when the browser should refuse this request.
    ///
    /// Anything that is not an absolute http(s) URL is allowed through
    /// untouched: refusing what we failed to parse would be a way to break a
    /// page for no reason.
    /// </summary>
    public static bool ShouldBlock(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        return IsBlockedHost(uri.Host);
    }

    /// <summary>
    /// Whether a host is the blocked domain itself or a subdomain of it. The
    /// leading dot in the suffix test is what keeps "notadform.net" out of it.
    /// </summary>
    internal static bool IsBlockedHost(string host)
    {
        string candidate = host.TrimEnd('.').ToLowerInvariant();

        foreach (string blocked in BlockedDomains)
        {
            if (candidate.Equals(blocked, StringComparison.Ordinal) ||
                candidate.EndsWith('.' + blocked, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
