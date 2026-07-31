using ElwMeteo.Core.Models;
using ElwMeteo.Core.Persistence;
using Xunit;

namespace ElwMeteo.Core.Tests;

public class SnapshotCacheTests : IDisposable
{
    private readonly string _path =
        Path.Combine(Path.GetTempPath(), $"elw-cache-{Guid.NewGuid():N}.json");

    private static readonly DateTimeOffset Noon =
        new(2026, 3, 14, 12, 0, 0, TimeSpan.Zero);

    public void Dispose()
    {
        foreach (string path in new[] { _path, _path + ".tmp" })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        GC.SuppressFinalize(this);
    }

    private SnapshotCache Cache() => new(_path);

    private static CachedState State(DateTimeOffset savedAt) => new()
    {
        Snapshot = new WeatherSnapshot
        {
            Position = new GeoPosition(50.1109, 8.6821, PositionSource.Gps, savedAt, AccuracyM: 4),
            RetrievedAtUtc = savedAt,
            TemperatureC = 7.5,
            WindSpeedMs = 6.2,
            WindDirectionDeg = 245,
            ModelName = "ICON-D2",
            Hourly =
            [
                new HourlyStep(savedAt, 7.5, 0.2, 40, 6.2, 9.1, 245, 80, 76, 12000, null, 61)
            ],
            Daily =
            [
                new DailySummary(savedAt.Date, 3.0, 11.0, 4.2, 2.0, 14.0)
            ]
        },
        Warnings =
        [
            new DwdWarning("STURMBÖEN", "Amtliche Warnung vor STURMBÖEN", WarningLevel.Moderate,
                savedAt, savedAt.AddHours(6), "Stadt Frankfurt am Main", "Böen bis 80 km/h.", "Bäume meiden.")
        ],
        AddressLine = "Römerberg 23, 60311 Frankfurt am Main",
        WarningSource = "DWD CAP über Bright Sky",
        SavedAtUtc = savedAt
    };

    [Fact]
    public void LoadReturnsNullWhenNothingHasBeenStored()
    {
        Assert.Null(Cache().Load(Noon));
    }

    [Fact]
    public void AStoredStateComesBackWithItsValues()
    {
        Assert.True(Cache().Save(State(Noon)));

        CachedState? loaded = Cache().Load(Noon.AddMinutes(41));

        Assert.NotNull(loaded);
        Assert.Equal(7.5, loaded.Snapshot.TemperatureC);
        Assert.Equal(245, loaded.Snapshot.WindDirectionDeg);
        Assert.Equal("ICON-D2", loaded.Snapshot.ModelName);
        Assert.Equal(PositionSource.Gps, loaded.Snapshot.Position.Source);
        Assert.Equal("Römerberg 23, 60311 Frankfurt am Main", loaded.AddressLine);
    }

    [Fact]
    public void TheHourlyAndDailySeriesSurviveTheRoundTrip()
    {
        Cache().Save(State(Noon));

        CachedState loaded = Cache().Load(Noon.AddMinutes(5))!;

        Assert.Single(loaded.Snapshot.Hourly);
        Assert.Equal(0.2, loaded.Snapshot.Hourly[0].PrecipitationMm);
        Assert.Single(loaded.Snapshot.Daily);
        Assert.Equal(11.0, loaded.Snapshot.Daily[0].TemperatureMaxC);
    }

    [Fact]
    public void WarningsSurviveWithTheirLevel()
    {
        Cache().Save(State(Noon));

        CachedState loaded = Cache().Load(Noon.AddMinutes(5))!;

        Assert.Single(loaded.Warnings);
        Assert.Equal(WarningLevel.Moderate, loaded.Warnings[0].Level);
        Assert.Equal("STURMBÖEN", loaded.Warnings[0].Event);
        Assert.Equal("Bäume meiden.", loaded.Warnings[0].Instruction);
    }

    /// <summary>
    /// The point of the whole feature: a wind direction from three days ago is
    /// not old data, it is wrong data, and offering it would be worse than the
    /// empty panel this is meant to replace.
    /// </summary>
    [Fact]
    public void AStateOlderThanTheLimitIsNotOffered()
    {
        Cache().Save(State(Noon));

        Assert.NotNull(Cache().Load(Noon + SnapshotCache.MaximumAge - TimeSpan.FromMinutes(1)));
        Assert.Null(Cache().Load(Noon + SnapshotCache.MaximumAge + TimeSpan.FromMinutes(1)));
    }

    /// <summary>A clock that jumped backwards must not produce a state that never expires.</summary>
    [Fact]
    public void AStateFromTheFutureIsRejected()
    {
        Cache().Save(State(Noon));

        Assert.Null(Cache().Load(Noon.AddHours(-3)));
    }

    [Fact]
    public void ACorruptFileIsIgnoredRatherThanThrown()
    {
        File.WriteAllText(_path, "{ this is not json");

        SnapshotCache cache = Cache();

        Assert.Null(cache.Load(Noon));
        Assert.NotNull(cache.LastError);
    }

    [Fact]
    public void AnEmptyFileIsIgnored()
    {
        File.WriteAllText(_path, string.Empty);

        Assert.Null(Cache().Load(Noon));
    }

    /// <summary>
    /// Written aside and moved into place, so a power cut mid-write cannot
    /// leave a truncated file behind — which would defeat the entire purpose.
    /// </summary>
    [Fact]
    public void SavingLeavesNoTemporaryFileBehind()
    {
        Cache().Save(State(Noon));

        Assert.True(File.Exists(_path));
        Assert.False(File.Exists(_path + ".tmp"));
    }

    [Fact]
    public void SavingTwiceReplacesTheStoredState()
    {
        Cache().Save(State(Noon));
        Cache().Save(State(Noon) with { AddressLine = "Zweiter Ort" });

        Assert.Equal("Zweiter Ort", Cache().Load(Noon)!.AddressLine);
    }

    [Theory]
    [InlineData(0, "gerade eben")]
    [InlineData(41, "vor 41 min")]
    [InlineData(125, "vor 2 h 05 min")]
    public void TheAgeIsWordedForTheBanner(int minutes, string expected)
    {
        Assert.Equal(expected, SnapshotCache.DescribeAge(TimeSpan.FromMinutes(minutes)));
    }
}
