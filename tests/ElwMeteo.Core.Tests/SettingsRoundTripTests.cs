using System.Reflection;
using ElwMeteo.Core.Configuration;
using ElwMeteo.Core.Services;
using ElwMeteo.Presentation.Platform;
using ElwMeteo.Presentation.Services;
using ElwMeteo.Presentation.ViewModels;
using Xunit;

namespace ElwMeteo.Core.Tests;

/// <summary>
/// Every switch on the settings page has to survive „Übernehmen".
///
/// This exists because of a defect that compiled cleanly. A merge dropped the
/// one line in <c>Apply</c> that copies a checkbox into the settings object,
/// and nothing anywhere complained: the checkbox still ticked, the page still
/// said „Einstellungen übernommen", and the setting was simply gone on the next
/// start. A test per setting would not have caught it either — nobody writes the
/// test for the setting they forgot. So the check is taken over every switch at
/// once, by reflection, and covers the ones added after this was written too.
/// </summary>
public class SettingsRoundTripTests : IDisposable
{
    private readonly string _path =
        Path.Combine(Path.GetTempPath(), $"elw-settings-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }

        GC.SuppressFinalize(this);
    }

    private sealed class SilentShell : IShellLauncher
    {
        public bool TryOpen(string target, out string? error)
        {
            error = null;
            return true;
        }
    }

    /// <summary>
    /// Settings whose view-model switch deliberately does not end up in the
    /// settings object, with the reason. Anything not named here must round-trip.
    /// </summary>
    private static readonly Dictionary<string, string> Exempt = new()
    {
        // Written only once the entry was really created, and set back to what is
        // actually registered when it could not be. Recording a state that was
        // never achieved is the failure this avoids.
        [nameof(AppSettings.AutostartEnabled)] =
            "folgt dem tatsächlich geschriebenen Autostart-Eintrag, nicht dem Häkchen"
    };

    private (SettingsViewModel Page, AppSettings Settings) Build()
    {
        AppSettings settings = new();
        SettingsStore store = new(settings, _path);
        using HttpClient http = new();

        return (new SettingsViewModel(
            store,
            new GpsSerialService(),
            new GeocodingService(http),
            new SilentShell(),
            nina: null,
            systemLocation: null,
            // Deliberately the unsupported stand-in: a real one would write into
            // the registry or the home directory of whoever runs the tests.
            autostart: new UnsupportedAutostartService("Im Test nicht verfügbar."),
            screens: new NoScreenService()),
            settings);
    }

    /// <summary>Switches that exist under the same name on both sides.</summary>
    private static IEnumerable<(PropertyInfo Page, PropertyInfo Setting)> SharedSwitches(
        SettingsViewModel page)
    {
        foreach (PropertyInfo pageProperty in page.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(bool) && p.CanRead && p.CanWrite))
        {
            PropertyInfo? setting = typeof(AppSettings).GetProperty(pageProperty.Name);

            if (setting is not null && setting.PropertyType == typeof(bool) && setting.CanWrite)
            {
                yield return (pageProperty, setting);
            }
        }
    }

    [Fact]
    public void ThereAreSwitchesToCheck()
    {
        // Guards the test itself: a reflection query that silently matches
        // nothing passes every assertion below without checking anything.
        (SettingsViewModel page, _) = Build();

        Assert.True(SharedSwitches(page).Count() >= 5);
    }

    [Fact]
    public void EverySwitchSurvivesApply()
    {
        (SettingsViewModel page, AppSettings settings) = Build();

        List<(PropertyInfo Page, PropertyInfo Setting, bool Wanted)> flipped = [];

        foreach ((PropertyInfo pageProperty, PropertyInfo setting) in SharedSwitches(page))
        {
            if (Exempt.ContainsKey(setting.Name))
            {
                continue;
            }

            // Away from whatever the default is, so a setting that is never
            // written cannot pass by accidentally already holding the value.
            bool wanted = !(bool)pageProperty.GetValue(page)!;
            pageProperty.SetValue(page, wanted);
            flipped.Add((pageProperty, setting, wanted));
        }

        page.ApplyCommand.Execute(null);

        List<string> lost = [.. flipped
            .Where(f => (bool)f.Setting.GetValue(settings)! != f.Wanted)
            .Select(f => f.Setting.Name)];

        Assert.Empty(lost);
    }

    [Fact]
    public void TheWebFilterSwitchSurvivesApply()
    {
        // Named rather than left to the sweep above: this is the one that was
        // actually lost, and a named test says so in its own failure message.
        (SettingsViewModel page, AppSettings settings) = Build();

        page.BlockWebTrackers = false;
        page.ApplyCommand.Execute(null);

        Assert.False(settings.BlockWebTrackers);

        page.BlockWebTrackers = true;
        page.ApplyCommand.Execute(null);

        Assert.True(settings.BlockWebTrackers);
    }

    [Fact]
    public void ApplyWritesTheFile()
    {
        (SettingsViewModel page, _) = Build();

        page.ApplyCommand.Execute(null);

        // The round trip above only proves the object was updated. Without the
        // file the whole exercise is lost at the next start anyway.
        Assert.True(File.Exists(_path));

        AppSettings reloaded = AppSettings.Load(_path);

        Assert.NotNull(reloaded);
    }

    [Fact]
    public void EveryExemptionStillNamesARealSetting()
    {
        // An exemption for a setting that no longer exists is a hole nobody
        // notices: the sweep stops covering something and the list looks fine.
        foreach (string name in Exempt.Keys)
        {
            Assert.NotNull(typeof(AppSettings).GetProperty(name));
        }
    }
}
