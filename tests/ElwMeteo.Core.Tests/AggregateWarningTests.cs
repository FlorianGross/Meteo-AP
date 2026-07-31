using ElwMeteo.Core.Models;
using ElwMeteo.Core.Services;
using Xunit;

namespace ElwMeteo.Core.Tests;

public class AggregateWarningTests
{
    private static readonly GeoPosition Somewhere =
        new(50.1109, 8.6821, PositionSource.Manual, DateTimeOffset.UnixEpoch);

    private sealed class Stub(params DwdWarning[] warnings) : IWarningProvider
    {
        public Task<IReadOnlyList<DwdWarning>> GetAsync(GeoPosition position, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DwdWarning>>(warnings);
    }

    private sealed class Broken(string message = "nicht erreichbar") : IWarningProvider
    {
        public Task<IReadOnlyList<DwdWarning>> GetAsync(GeoPosition position, CancellationToken cancellationToken = default) =>
            throw new WarningProviderException(message);
    }

    private static DwdWarning Warning(string name, WarningLevel level) =>
        new(name, name, level, null, null, "—", null, null);

    [Fact]
    public async Task WarningsFromEverySourceEndUpInOneList()
    {
        var aggregate = new AggregateWarningProvider(
            new Stub(Warning("STURMBÖEN", WarningLevel.Moderate)),
            new Stub(Warning("Gefahrstoffaustritt", WarningLevel.Severe)));

        IReadOnlyList<DwdWarning> warnings = await aggregate.GetAsync(Somewhere);

        Assert.Equal(2, warnings.Count);
    }

    [Fact]
    public async Task TheWorstOneComesFirstSoTheBannerIsRight()
    {
        var aggregate = new AggregateWarningProvider(
            new Stub(Warning("GEWITTER", WarningLevel.Minor)),
            new Stub(Warning("Gefahrstoffaustritt", WarningLevel.Extreme)));

        IReadOnlyList<DwdWarning> warnings = await aggregate.GetAsync(Somewhere);

        Assert.Equal(WarningLevel.Extreme, warnings[0].Level);
    }

    /// <summary>
    /// The point of aggregating rather than falling back: if NINA is down the
    /// storm warning still arrives, and the other way round.
    /// </summary>
    [Fact]
    public async Task OneBrokenSourceDoesNotSuppressTheOther()
    {
        var aggregate = new AggregateWarningProvider(
            new Stub(Warning("STURMBÖEN", WarningLevel.Moderate)),
            new Broken());

        IReadOnlyList<DwdWarning> warnings = await aggregate.GetAsync(Somewhere);

        Assert.Single(warnings);
        Assert.Single(aggregate.LastFailures);
    }

    /// <summary>
    /// „No warnings" and „could not ask" look identical on a panel and mean
    /// opposite things, so the second one has to be an exception.
    /// </summary>
    [Fact]
    public async Task WhenEverySourceFailsTheFailureIsReported()
    {
        var aggregate = new AggregateWarningProvider(new Broken("DWD weg"), new Broken("NINA weg"));

        WarningProviderException error = await Assert.ThrowsAsync<WarningProviderException>(
            () => aggregate.GetAsync(Somewhere));

        Assert.Contains("DWD weg", error.Message, StringComparison.Ordinal);
        Assert.Contains("NINA weg", error.Message, StringComparison.Ordinal);
        Assert.Equal("keine Quelle erreichbar", aggregate.LastSourceLabel);
    }

    [Fact]
    public async Task ASourceThatAnswersEmptyIsStillASourceThatAnswered()
    {
        var aggregate = new AggregateWarningProvider(new Stub(), new Broken());

        Assert.Empty(await aggregate.GetAsync(Somewhere));
        Assert.NotEqual("keine Quelle erreichbar", aggregate.LastSourceLabel);
    }

    [Fact]
    public async Task TheSourceLineNamesHowManyRoutesFailed()
    {
        var aggregate = new AggregateWarningProvider(new Stub(), new Broken());

        await aggregate.GetAsync(Somewhere);

        Assert.Contains("nicht erreichbar: 1", aggregate.LastSourceLabel, StringComparison.Ordinal);
    }
}
