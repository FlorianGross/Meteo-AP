using ElwMeteo.Core.Maps;
using Xunit;

namespace ElwMeteo.Core.Tests;

/// <summary>
/// A content filter fails in two directions, and only one of them is visible.
/// Letting an advert through is a nuisance. Blocking a tile server leaves a grey
/// rectangle that looks like a broken application, and nobody connects it to a
/// setting they switched on weeks ago — so most of this file is about the second
/// kind.
/// </summary>
public class WebContentFilterTests
{
    [Theory]
    [InlineData("https://pagead2.googlesyndication.com/pagead/js/adsbygoogle.js")]
    [InlineData("https://www.google-analytics.com/collect")]
    [InlineData("https://stats.g.doubleclick.net/j/collect")]
    [InlineData("https://ib.adnxs.com/ut/v3/prebid")]
    [InlineData("https://cdn.taboola.com/libtrc/loader.js")]
    [InlineData("https://script.ioam.de/iam.js")]
    [InlineData("https://connect.facebook.net/en_US/fbevents.js")]
    public void KnownAdAndTrackingHostsAreBlocked(string url)
    {
        Assert.True(WebContentFilter.ShouldBlock(url), url);
    }

    /// <summary>
    /// The whole point is that the provider's page still works. Every host the
    /// shipped viewers actually need is checked by name.
    /// </summary>
    [Theory]
    [InlineData("https://www.rainviewer.com/map.html")]
    [InlineData("https://tilecache.rainviewer.com/v2/radar/0/256/4/8/5/1_1.png")]
    [InlineData("https://www.windy.com/-Radar-radar")]
    [InlineData("https://www.ventusky.com/?p=50;8;9")]
    [InlineData("https://kachelmannwetter.com/de/gewitter")]
    [InlineData("https://kachelmannwetter.com/de/radarprognose")]
    [InlineData("https://www.dwd.de/DE/wetter/warnungen_gemeinden/warnWetter_node.html")]
    [InlineData("https://maps.dwd.de/geoserver/dwd/ows")]
    [InlineData("https://warnung.bund.de/karte")]
    [InlineData("https://map.blitzortung.org/")]
    [InlineData("https://metradar.ch/de/loop_aktuell.php")]
    [InlineData("https://www.meteoblue.com/de/wetter/karte/niederschlag/germany")]
    [InlineData("https://tile.openstreetmap.org/10/535/337.png")]
    [InlineData("https://api.open-meteo.com/v1/forecast")]
    [InlineData("https://api.brightsky.dev/current_weather")]
    public void TheViewersOwnHostsAreNeverBlocked(string url)
    {
        Assert.False(WebContentFilter.ShouldBlock(url), url);
    }

    /// <summary>
    /// Every built-in viewer's host, taken from the catalogue rather than typed
    /// out — so adding a viewer that collides with the block list fails here
    /// instead of on a vehicle.
    /// </summary>
    [Fact]
    public void NoBuiltInViewerIsBlockedByItsOwnApplication()
    {
        foreach (WebViewSource source in WebViewSourceCatalog.BuiltIn)
        {
            string resolved = source.Resolve(50.1109, 8.6821, 9);

            Assert.False(WebContentFilter.ShouldBlock(resolved),
                $"„{source.Title}“ würde von der eigenen Sperrliste blockiert: {resolved}");
        }
    }

    /// <summary>
    /// Blocking a consent manager leaves the page grey behind an overlay that
    /// can no longer be dismissed — strictly worse than the dialogue itself.
    /// </summary>
    [Theory]
    [InlineData("https://cdn.cookielaw.org/scripttemplates/otSDKStub.js")]
    [InlineData("https://app.usercentrics.eu/browser-ui/latest/loader.js")]
    [InlineData("https://consent.cookiebot.com/uc.js")]
    [InlineData("https://cdn.privacy-mgmt.com/wrapperMessagingWithoutDetection.js")]
    public void ConsentManagersAreLeftAlone(string url)
    {
        Assert.False(WebContentFilter.ShouldBlock(url), url);
    }

    /// <summary>
    /// Suffix matching, not substring: a host that merely ends in the same
    /// letters is a different company.
    /// </summary>
    [Theory]
    [InlineData("https://notadform.net/x.js")]
    [InlineData("https://criteo.com.example.org/x.js")]
    [InlineData("https://myoutbrain.com/x.js")]
    public void ASimilarLookingHostIsNotCaught(string url)
    {
        Assert.False(WebContentFilter.ShouldBlock(url), url);
    }

    [Theory]
    [InlineData("https://ADNXS.COM/x")]
    [InlineData("https://Ib.AdNxS.cOm/x")]
    [InlineData("https://ib.adnxs.com./x")]
    public void MatchingIgnoresCaseAndTheTrailingDot(string url)
    {
        Assert.True(WebContentFilter.ShouldBlock(url), url);
    }

    /// <summary>Anything unparseable is let through — refusing it would break pages for no reason.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("nicht-einmal-eine-url")]
    [InlineData("/relativ/pfad.js")]
    [InlineData("data:text/javascript,alert(1)")]
    [InlineData("blob:https://example.org/abc")]
    public void WhatCannotBeParsedIsAllowed(string? url)
    {
        Assert.False(WebContentFilter.ShouldBlock(url));
    }

    [Fact]
    public void TheListHasNoDuplicatesAndNoStrayDots()
    {
        var domains = WebContentFilter.BlockedDomains;

        Assert.Equal(domains.Count, domains.Distinct(StringComparer.Ordinal).Count());
        Assert.All(domains, d => Assert.False(d.StartsWith('.') || d.EndsWith('.'), d));
        Assert.All(domains, d => Assert.Equal(d.ToLowerInvariant(), d));
        Assert.All(domains, d => Assert.Contains('.', d));
    }
}
