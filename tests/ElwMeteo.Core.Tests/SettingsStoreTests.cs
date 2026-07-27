using ElwMeteo.Core.Configuration;
using Xunit;

namespace ElwMeteo.Core.Tests;

public class SettingsStoreTests : IDisposable
{
    private readonly string _path =
        Path.Combine(Path.GetTempPath(), $"elw-store-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }

        GC.SuppressFinalize(this);
    }

    private SettingsStore Store(AppSettings? settings = null) =>
        new(settings ?? new AppSettings(), _path);

    [Fact]
    public void Flush_WritesNothingWhenNothingChanged()
    {
        SettingsStore store = Store();

        Assert.False(store.Flush());
        Assert.False(File.Exists(_path));
    }

    [Fact]
    public void RequestSave_ThenFlush_WritesTheFile()
    {
        SettingsStore store = Store();
        store.Settings.RadarMaxZoom = 9;

        store.RequestSave();

        Assert.True(store.IsDirty);
        Assert.True(store.Flush());
        Assert.Equal(9, AppSettings.Load(_path).RadarMaxZoom);
    }

    [Fact]
    public void ManyChangesCollapseIntoOneWrite()
    {
        SettingsStore store = Store();

        // The whole point: a slider drag is dozens of changes and must not be
        // dozens of file writes.
        for (int i = 0; i < 50; i++)
        {
            store.Settings.RadarMaxZoom = i % 12 + 4;
            store.RequestSave();
        }

        Assert.True(store.Flush());

        // Second flush has nothing left to do.
        Assert.False(store.Flush());
        Assert.False(store.IsDirty);
    }

    [Fact]
    public void Flush_KeepsTheLastValue()
    {
        SettingsStore store = Store();

        store.Settings.MapZoom = 8;
        store.RequestSave();
        store.Settings.MapZoom = 13;
        store.RequestSave();
        store.Flush();

        Assert.Equal(13, AppSettings.Load(_path).MapZoom);
    }

    [Fact]
    public void SaveNow_WritesEvenWithoutAPriorRequest()
    {
        SettingsStore store = Store();
        store.Settings.HomeName = "Wache 3";

        Assert.True(store.SaveNow());
        Assert.Equal("Wache 3", AppSettings.Load(_path).HomeName);
    }

    [Fact]
    public void AFailedWriteIsReportedRatherThanThrown()
    {
        // A directory where the file should be: the write cannot succeed, and a
        // full disk or a read-only profile must not take the application down.
        Directory.CreateDirectory(_path);

        SettingsStore store = Store();
        store.RequestSave();

        Assert.False(store.Flush());
        Assert.NotNull(store.LastError);

        Directory.Delete(_path);
    }

    [Fact]
    public void AFailedWriteRaisesSaveFailedOnce()
    {
        Directory.CreateDirectory(_path);

        SettingsStore store = Store();
        int calls = 0;
        store.SaveFailed += _ => calls++;

        store.RequestSave();
        store.Flush();

        // Not retried on the next tick: the same broken write every two seconds
        // would only fill the log. The next real change marks it dirty again.
        store.Flush();

        Assert.Equal(1, calls);

        Directory.Delete(_path);
    }

    [Fact]
    public void ASuccessfulWriteClearsAnEarlierError()
    {
        Directory.CreateDirectory(_path);

        SettingsStore store = Store();
        store.RequestSave();
        store.Flush();
        Assert.NotNull(store.LastError);

        Directory.Delete(_path);

        store.RequestSave();

        Assert.True(store.Flush());
        Assert.Null(store.LastError);
    }

    [Fact]
    public void StoreAndSettingsAreTheSameObject()
    {
        var settings = new AppSettings();

        // View models edit the settings directly and only mark them dirty; if
        // the store held a copy those edits would never be written.
        Assert.Same(settings, Store(settings).Settings);
    }
}
