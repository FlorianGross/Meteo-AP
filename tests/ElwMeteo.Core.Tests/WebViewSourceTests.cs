using System.Globalization;
using ElwMeteo.Core.Configuration;
using ElwMeteo.Core.Maps;
using Xunit;

namespace ElwMeteo.Core.Tests;

public class WebViewSourceTests
{
    private static WebViewSource Source(string template) =>
        new() { Id = "x", Title = "X", UrlTemplate = template };

    [Fact]
    public void Resolve_SubstitutesEveryPlaceholder()
    {
        string url = Source("https://x.example/?p={lat};{lon};{zoom}").Resolve(50.1109, 8.6821, 11);

        Assert.Equal("https://x.example/?p=50.11090;8.68210;11", url);
    }

    [Fact]
    public void Resolve_UsesADecimalPointRegardlessOfTheThreadCulture()
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");

        try
        {
            // A German decimal comma would silently break the query string, and
            // the app runs on German machines by definition.
            string url = Source("https://x.example/?lat={lat}").Resolve(50.5, 8.5, 9);

            Assert.Equal("https://x.example/?lat=50.50000", url);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Resolve_LeavesATemplateWithoutPlaceholdersAlone()
    {
        Assert.Equal("https://x.example/karte", Source("https://x.example/karte").Resolve(50.0, 8.0, 9));
    }

    [Fact]
    public void FollowsPosition_IsTrueOnlyWhenTheUrlCarriesCoordinates()
    {
        Assert.True(Source("https://x.example/#{zoom}/{lat}/{lon}").FollowsPosition);
        Assert.True(Source("https://x.example/?lat={lat}").FollowsPosition);
        Assert.False(Source("https://x.example/karte").FollowsPosition);

        // Zoom alone must not count: re-navigating on a position update would
        // throw away whatever the operator had panned to.
        Assert.False(Source("https://x.example/?z={zoom}").FollowsPosition);
    }

    [Theory]
    [InlineData("https://x.example/")]
    [InlineData("http://x.example/")]
    [InlineData("https://x.example/?p={lat};{lon};{zoom}")]
    public void IsAcceptableUrl_AllowsTheTwoWebSchemes(string url)
    {
        Assert.True(WebViewSource.IsAcceptableUrl(url));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("x.example/karte")]
    [InlineData("file:///C:/Windows/win.ini")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<h1>x</h1>")]
    [InlineData("ftp://x.example/")]
    public void IsAcceptableUrl_RejectsEverythingElse(string? url)
    {
        // A settings file can be hand-edited, so a local path or a script URL
        // must never be navigated to just because it was typed in.
        Assert.False(WebViewSource.IsAcceptableUrl(url));
    }
}

public class WebViewSourceCatalogTests
{
    [Fact]
    public void BuiltIn_UrlsAreAllAcceptable()
    {
        Assert.All(WebViewSourceCatalog.BuiltIn,
            s => Assert.True(WebViewSource.IsAcceptableUrl(s.UrlTemplate), s.Id));
    }

    [Fact]
    public void BuiltIn_IdsAreUnique()
    {
        var ids = WebViewSourceCatalog.BuiltIn.Select(s => s.Id).ToList();

        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void BuiltIn_AllCarryATitleGroupAndDescription()
    {
        Assert.All(WebViewSourceCatalog.BuiltIn, s =>
        {
            Assert.False(string.IsNullOrWhiteSpace(s.Title));
            Assert.False(string.IsNullOrWhiteSpace(s.Group));
            Assert.False(string.IsNullOrWhiteSpace(s.Description));
            Assert.True(s.IsBuiltIn);
        });
    }

    [Fact]
    public void BuiltIn_CoversRadarOfficialWarningsAndLightning()
    {
        var groups = WebViewSourceCatalog.BuiltIn.Select(s => s.Group).Distinct().ToList();

        Assert.Contains("Regenradar", groups);
        Assert.Contains("Amtlich (DWD)", groups);
        Assert.Contains("Gewitter", groups);
    }

    [Fact]
    public void BuiltIn_ResolveProducesAValidAbsoluteUrlForEachEntry()
    {
        Assert.All(WebViewSourceCatalog.BuiltIn, s =>
        {
            string url = s.Resolve(50.1109, 8.6821, 11);

            Assert.True(Uri.TryCreate(url, UriKind.Absolute, out Uri? uri), s.Id);
            Assert.Equal("https", uri!.Scheme);

            // A leftover placeholder means a typo in the catalogue.
            Assert.DoesNotContain("{lat}", url, StringComparison.Ordinal);
            Assert.DoesNotContain("{lon}", url, StringComparison.Ordinal);
            Assert.DoesNotContain("{zoom}", url, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void BuiltIn_EnoughViewersOpenAtThePosition()
    {
        // Several providers only publish a fixed national or regional page, and
        // adding one of those is a legitimate thing to do — so this is not a
        // majority rule. What has to stay true is that the operator always has a
        // decent choice of viewers that land on the incident instead of on a
        // country overview.
        int following = WebViewSourceCatalog.BuiltIn.Count(s => s.FollowsPosition);

        Assert.True(following >= 5, $"nur {following} Ansichten folgen der Position");
    }

    /// <summary>
    /// Whatever else the list contains, the one that opens without being asked
    /// has to land on the incident — otherwise the tab greets a crew with a map
    /// of Germany.
    /// </summary>
    [Fact]
    public void TheDefaultViewerFollowsThePosition()
    {
        string defaultId = new Configuration.AppSettings().SelectedWebSourceId;
        WebViewSource? viewer = WebViewSourceCatalog.ById(defaultId);

        Assert.NotNull(viewer);
        Assert.True(viewer.FollowsPosition, $"Voreinstellung „{defaultId}“ folgt der Position nicht");
    }

    [Fact]
    public void TheMeteoblueEntriesUseThePathThatActuallyExists()
    {
        // Only "weather" is translated in meteoblue's German URLs — the map
        // segment stays "maps". The original /de/wetter/karten/index pointed at
        // a page that does not exist, which is why that entry never worked.
        Assert.All(
            WebViewSourceCatalog.BuiltIn.Where(s => s.Id.StartsWith("meteoblue", StringComparison.Ordinal)),
            s => Assert.DoesNotContain("/wetter/karten/", s.UrlTemplate, StringComparison.Ordinal));
    }

    [Fact]
    public void OneMeteoblueEntryWorksWithoutFollowingThePosition()
    {
        // Whether the coordinate fragment is honoured on the index page could
        // not be verified, so a fixed map that is known to exist stays in the
        // list beside it.
        var entries = WebViewSourceCatalog.BuiltIn
            .Where(s => s.Id.StartsWith("meteoblue", StringComparison.Ordinal))
            .ToList();

        Assert.Contains(entries, s => !s.FollowsPosition);
        Assert.Contains(entries, s => s.FollowsPosition);
    }

    [Fact]
    public void ById_FindsABuiltInAndReturnsNullOtherwise()
    {
        Assert.NotNull(WebViewSourceCatalog.ById("rainviewer-web"));
        Assert.Null(WebViewSourceCatalog.ById("does-not-exist"));
        Assert.Null(WebViewSourceCatalog.ById(null));
    }

    [Fact]
    public void TheDefaultSelectionInSettingsExists()
    {
        // A default naming a viewer that was renamed would open an empty tab.
        Assert.NotNull(WebViewSourceCatalog.ById(new AppSettings().SelectedWebSourceId));
    }
}

public class CustomWebSourceSettingsTests
{
    [Fact]
    public void CustomSourcesSurviveARoundTripThroughTheSettingsFile()
    {
        string path = Path.Combine(Path.GetTempPath(), $"elw-web-{Guid.NewGuid():N}.json");

        try
        {
            var settings = new AppSettings
            {
                CustomWebSources =
                {
                    new CustomWebSource { Name = "Eigenes Radar", UrlTemplate = "https://x.example/?p={lat};{lon}" }
                },
                EnabledWebSourceIds = ["rainviewer-web", "blitzortung"],
                SelectedWebSourceId = "blitzortung",
                WebSourceZoom = 12
            };

            settings.Save(path);
            AppSettings loaded = AppSettings.Load(path);

            Assert.Single(loaded.CustomWebSources);
            Assert.Equal("Eigenes Radar", loaded.CustomWebSources[0].Name);
            Assert.Equal("https://x.example/?p={lat};{lon}", loaded.CustomWebSources[0].UrlTemplate);
            Assert.Equal(["rainviewer-web", "blitzortung"], loaded.EnabledWebSourceIds);
            Assert.Equal("blitzortung", loaded.SelectedWebSourceId);
            Assert.Equal(12, loaded.WebSourceZoom);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ASettingsFileWrittenBeforeTheBrowserTabStillLoads()
    {
        string path = Path.Combine(Path.GetTempPath(), $"elw-web-{Guid.NewGuid():N}.json");

        try
        {
            // Only fields that existed before this feature — an older file must
            // not keep the application from starting.
            File.WriteAllText(path, """{ "HomeName": "Feuerwache", "MapZoom": 11 }""");

            AppSettings loaded = AppSettings.Load(path);

            Assert.Empty(loaded.CustomWebSources);
            Assert.Empty(loaded.EnabledWebSourceIds);
            Assert.Equal("rainviewer-web", loaded.SelectedWebSourceId);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
