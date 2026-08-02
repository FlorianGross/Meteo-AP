using ElwMeteo.Core.Configuration;
using ElwMeteo.Core.Models;
using ElwMeteo.Core.Services;
using ElwMeteo.Presentation.Platform;
using ElwMeteo.Presentation.Services;
using Xunit;

namespace ElwMeteo.Core.Tests;

/// <summary>
/// The fallback ladder, which decides which number the whole application then
/// treats as „the incident position". Getting the order wrong does not fail
/// loudly — it just quietly answers with a worse position.
/// </summary>
public class LocationResolverTests
{
    private sealed class StubSystemLocation(GeoPosition? position, SystemLocationState state = SystemLocationState.Allowed)
        : ISystemLocationProvider
    {
        public int Calls { get; private set; }

        public string Name => "Teststandort";

        public SystemLocationState State => state;

        public string StatusText => "Test";

        public Task<SystemLocationState> RequestAccessAsync() => Task.FromResult(state);

        public Task<GeoPosition?> GetAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(position);
        }
    }

    private sealed class ThrowingSystemLocation : ISystemLocationProvider
    {
        public string Name => "Kaputt";

        public SystemLocationState State => SystemLocationState.Allowed;

        public string StatusText => "Test";

        public Task<SystemLocationState> RequestAccessAsync() =>
            Task.FromResult(SystemLocationState.Allowed);

        public Task<GeoPosition?> GetAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Standortdienst hängt");
    }

    private static AppSettings Settings(LocationMode mode = LocationMode.Automatic, bool useSystem = true) => new()
    {
        LocationMode = mode,
        UseSystemLocation = useSystem,
        HomeLatitude = 50.0,
        HomeLongitude = 8.0,
        HomeName = "Feuerwache",
        ManualLatitude = 51.0,
        ManualLongitude = 9.0
    };

    private static GeoPosition System(double accuracy) => new(
        48.1372, 11.5755, PositionSource.SystemService, DateTimeOffset.UtcNow, AccuracyM: accuracy);

    /// <summary>
    /// IpLocationProvider needs an HttpClient; none of these tests let it get
    /// that far, and where they do the request simply fails and returns null,
    /// which is the behaviour under test.
    /// </summary>
    private static LocationResolver Resolver(AppSettings settings, ISystemLocationProvider? system) =>
        new(settings, new GpsSerialService(), new IpLocationProvider(new HttpClient()), system);

    [Fact]
    public async Task WithoutAGpsFixTheSystemPositionIsUsed()
    {
        var stub = new StubSystemLocation(System(accuracy: 25));

        GeoPosition position = await Resolver(Settings(), stub).ResolveAsync();

        Assert.Equal(PositionSource.SystemService, position.Source);
        Assert.Equal(48.1372, position.Latitude, 4);
        Assert.Equal(1, stub.Calls);
    }

    /// <summary>
    /// The serial receiver stays the better source where there is one: metre
    /// accuracy, no network, no permission prompt.
    /// </summary>
    [Fact]
    public async Task ManualModeNeverAsksTheSystem()
    {
        var stub = new StubSystemLocation(System(accuracy: 5));

        GeoPosition position = await Resolver(Settings(LocationMode.Manual), stub).ResolveAsync();

        Assert.Equal(PositionSource.Manual, position.Source);
        Assert.Equal(0, stub.Calls);
    }

    /// <summary>„GPS only" means what it says — no quiet substitution.</summary>
    [Fact]
    public async Task GpsOnlyModeFallsBackToTheStationRatherThanTheSystem()
    {
        var stub = new StubSystemLocation(System(accuracy: 5));

        GeoPosition position = await Resolver(Settings(LocationMode.GpsOnly), stub).ResolveAsync();

        Assert.Equal(PositionSource.Preset, position.Source);
        Assert.Equal(0, stub.Calls);
    }

    [Fact]
    public async Task TheSwitchInTheSettingsIsHonoured()
    {
        var stub = new StubSystemLocation(System(accuracy: 5));

        GeoPosition position = await Resolver(Settings(useSystem: false), stub).ResolveAsync();

        Assert.Equal(0, stub.Calls);
        Assert.NotEqual(PositionSource.SystemService, position.Source);
    }

    /// <summary>
    /// A radius of tens of kilometres means Windows fell back to the IP address
    /// itself. Passing that on under a name that sounds like a sensor reading
    /// would be worse than the IP lookup, which at least says what it is.
    /// </summary>
    [Fact]
    public async Task AUselesslyCoarsePositionIsRejected()
    {
        var stub = new StubSystemLocation(System(accuracy: 80_000));

        GeoPosition position = await Resolver(Settings(), stub).ResolveAsync();

        Assert.NotEqual(PositionSource.SystemService, position.Source);
    }

    [Fact]
    public async Task ACoarseButStillLocalPositionIsAccepted()
    {
        var stub = new StubSystemLocation(System(accuracy: 3_000));

        GeoPosition position = await Resolver(Settings(), stub).ResolveAsync();

        Assert.Equal(PositionSource.SystemService, position.Source);
    }

    [Fact]
    public async Task NullIsleadsToTheNextRungRatherThanAnException()
    {
        var stub = new StubSystemLocation(position: null);

        GeoPosition position = await Resolver(Settings(), stub).ResolveAsync();

        Assert.Equal(PositionSource.Preset, position.Source);
    }

    [Fact]
    public async Task AnImplausiblePositionIsRejected()
    {
        var stub = new StubSystemLocation(
            new GeoPosition(0, 0, PositionSource.SystemService, DateTimeOffset.UtcNow, AccuracyM: 10));

        GeoPosition position = await Resolver(Settings(), stub).ResolveAsync();

        Assert.NotEqual(PositionSource.SystemService, position.Source);
    }

    /// <summary>
    /// A location stack that throws must not take the weather refresh with it —
    /// there are two more rungs below it.
    /// </summary>
    [Fact]
    public async Task AFailingLocationServiceDoesNotBreakTheRefresh()
    {
        GeoPosition position = await Resolver(Settings(), new ThrowingSystemLocation()).ResolveAsync();

        Assert.Equal(PositionSource.Preset, position.Source);
    }

    [Fact]
    public async Task WithoutAProviderTheChainStillResolves()
    {
        GeoPosition position = await Resolver(Settings(), system: null).ResolveAsync();

        Assert.Equal(PositionSource.Preset, position.Source);
    }

    [Fact]
    public async Task TheDecisionIsRecordedForDiagnostics()
    {
        var resolver = Resolver(Settings(), new StubSystemLocation(System(accuracy: 12)));

        Assert.Equal("noch nichts abgerufen", resolver.LastDecision);

        await resolver.ResolveAsync();

        Assert.Contains("Teststandort", resolver.LastDecision, StringComparison.Ordinal);
    }

    /// <summary>
    /// The label is the only thing standing between an operator and a position
    /// that looks like a sensor reading but is a guess.
    /// </summary>
    [Theory]
    [InlineData(8, "Windows-Ortung ±8 m")]
    [InlineData(2400, "Windows-Ortung ±2400 m")]
    public void TheLabelCarriesTheRadius(double accuracy, string expected)
    {
        var position = new GeoPosition(
            48.0, 11.0, PositionSource.SystemService, DateTimeOffset.UtcNow, AccuracyM: accuracy);

        Assert.Equal(expected, position.SourceLabel);
    }

    [Fact]
    public void WithoutARadiusTheLabelSaysSo()
    {
        var position = new GeoPosition(48.0, 11.0, PositionSource.SystemService, DateTimeOffset.UtcNow);

        Assert.Contains("unbekannt", position.SourceLabel, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheUnsupportedProviderAnswersWithoutThrowing()
    {
        var provider = new UnsupportedSystemLocationProvider("kein Dienst auf dieser Ausgabe");

        Assert.Equal(SystemLocationState.Unsupported, provider.State);
        Assert.Equal("kein Dienst auf dieser Ausgabe", provider.StatusText);
        Assert.Null(await provider.GetAsync());
        Assert.Equal(SystemLocationState.Unsupported, await provider.RequestAccessAsync());
    }
}
