using ElwMeteo.Core.Diagnostics;
using Xunit;

namespace ElwMeteo.Core.Tests;

public class RequestLogTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 27, 12, 0, 0, TimeSpan.Zero);

    private static RequestRecord Record(string url, int? status, RequestOutcome outcome) =>
        new(Now, "GET", url, status, outcome, TimeSpan.FromMilliseconds(120), 1024, null);

    [Fact]
    public void Snapshot_ReturnsNewestFirst()
    {
        var log = new RequestLog();
        log.Add(Record("https://a.example/1", 200, RequestOutcome.Success));
        log.Add(Record("https://a.example/2", 200, RequestOutcome.Success));

        var snapshot = log.Snapshot();

        Assert.Equal("https://a.example/2", snapshot[0].Url);
        Assert.Equal("https://a.example/1", snapshot[1].Url);
    }

    [Fact]
    public void Add_DiscardsTheOldestBeyondCapacity()
    {
        var log = new RequestLog(capacity: 10);

        for (int i = 0; i < 40; i++)
        {
            log.Add(Record($"https://a.example/{i}", 200, RequestOutcome.Success));
        }

        var snapshot = log.Snapshot();

        Assert.Equal(10, snapshot.Count);
        Assert.Equal("https://a.example/39", snapshot[0].Url);
    }

    [Fact]
    public void Capacity_HasAFloorSoTheLogIsNeverUseless()
    {
        Assert.Equal(10, new RequestLog(capacity: 1).Capacity);
    }

    [Fact]
    public void Recorded_FiresForEachEntry()
    {
        var log = new RequestLog();
        int calls = 0;
        log.Recorded += _ => calls++;

        log.Add(Record("https://a.example/1", 200, RequestOutcome.Success));
        log.Add(Record("https://a.example/2", 500, RequestOutcome.HttpError));

        Assert.Equal(2, calls);
    }

    [Fact]
    public void HostSummary_CountsFailuresPerHost()
    {
        var log = new RequestLog();
        log.Add(Record("https://maps.dwd.de/geoserver", 500, RequestOutcome.HttpError));
        log.Add(Record("https://maps.dwd.de/geoserver", 500, RequestOutcome.HttpError));
        log.Add(Record("https://api.open-meteo.com/v1", 200, RequestOutcome.Success));

        var summary = log.HostSummary();

        // Worst host first — that is the one worth reading about.
        Assert.Equal("maps.dwd.de", summary[0].Host);
        Assert.Equal(2, summary[0].Failures);
        Assert.Equal(2, summary[0].Total);

        Assert.Equal("api.open-meteo.com", summary[1].Host);
        Assert.Equal(0, summary[1].Failures);
    }

    [Fact]
    public void Clear_EmptiesTheLog()
    {
        var log = new RequestLog();
        log.Add(Record("https://a.example/1", 200, RequestOutcome.Success));

        log.Clear();

        Assert.Empty(log.Snapshot());
    }

    [Fact]
    public void ToPlainText_ContainsTheFullUrlAndStatus()
    {
        var log = new RequestLog();
        log.Add(Record("https://maps.dwd.de/geoserver/dwd/wms?request=GetCapabilities", 404, RequestOutcome.HttpError));

        string text = log.ToPlainText(Now);

        // The full URL is the whole point: without it there is nothing to reproduce.
        Assert.Contains("https://maps.dwd.de/geoserver/dwd/wms?request=GetCapabilities", text);
        Assert.Contains("404", text);
        Assert.Contains("ÜBERSICHT", text);
    }

    [Fact]
    public void ToPlainText_SaysSoWhenNothingWasRecorded()
    {
        Assert.Contains("noch keine Anfragen", new RequestLog().ToPlainText(Now));
    }

    [Fact]
    public void ForResponse_TreatsTwoAndThreeHundredsAsSuccess()
    {
        Assert.Equal(RequestOutcome.Success,
            RequestLog.ForResponse("GET", "u", 200, TimeSpan.Zero, null, "OK").Outcome);
        Assert.Equal(RequestOutcome.Success,
            RequestLog.ForResponse("GET", "u", 304, TimeSpan.Zero, null, "Not Modified").Outcome);
        Assert.Equal(RequestOutcome.HttpError,
            RequestLog.ForResponse("GET", "u", 404, TimeSpan.Zero, null, "Not Found").Outcome);
        Assert.Equal(RequestOutcome.HttpError,
            RequestLog.ForResponse("GET", "u", 500, TimeSpan.Zero, null, "Server Error").Outcome);
    }

    [Fact]
    public void ForResponse_KeepsTheReasonOnlyForFailures()
    {
        Assert.Null(RequestLog.ForResponse("GET", "u", 200, TimeSpan.Zero, null, "OK").Detail);
        Assert.Equal("Not Found",
            RequestLog.ForResponse("GET", "u", 404, TimeSpan.Zero, null, "Not Found").Detail);
    }

    [Fact]
    public void ForException_SeparatesACallerCancellationFromATimeout()
    {
        RequestRecord cancelled = RequestLog.ForException(
            "GET", "u", TimeSpan.Zero, new TaskCanceledException(), wasCancelledByCaller: true);
        RequestRecord timedOut = RequestLog.ForException(
            "GET", "u", TimeSpan.Zero, new TaskCanceledException(), wasCancelledByCaller: false);

        Assert.Equal(RequestOutcome.Cancelled, cancelled.Outcome);
        Assert.Equal(RequestOutcome.Timeout, timedOut.Outcome);
    }

    [Fact]
    public void ForException_ReportsTheInnermostMessage()
    {
        // The outer message is generic; the inner one names the real cause.
        var exception = new HttpRequestException(
            "An error occurred while sending the request.",
            new IOException("Der Remotename konnte nicht aufgelöst werden."));

        RequestRecord record = RequestLog.ForException(
            "GET", "https://maps.dwd.de", TimeSpan.Zero, exception, wasCancelledByCaller: false);

        Assert.Equal(RequestOutcome.NetworkError, record.Outcome);
        Assert.Equal("Der Remotename konnte nicht aufgelöst werden.", record.Detail);
    }

    [Fact]
    public void AddExternalFailure_RecordsWhatNeverWentThroughHttpClient()
    {
        var log = new RequestLog();
        log.AddExternalFailure("Bahnanlagen", "Kacheln konnten nicht geladen werden.");

        RequestRecord record = log.Snapshot()[0];

        Assert.Equal("MAP", record.Method);
        Assert.Equal("Bahnanlagen", record.Url);
        Assert.True(record.IsFailure);
    }

    [Fact]
    public void Host_FallsBackForANonUrlEntry()
    {
        Assert.Equal("—", Record("Bahnanlagen", null, RequestOutcome.NetworkError).Host);
        Assert.Equal("api.brightsky.dev", Record("https://api.brightsky.dev/x", 200, RequestOutcome.Success).Host);
    }

    [Fact]
    public void StatusLabel_ReadsAsGermanPlainText()
    {
        Assert.Equal("200 OK", Record("u", 200, RequestOutcome.Success).StatusLabel);
        Assert.Equal("404 Fehler", Record("u", 404, RequestOutcome.HttpError).StatusLabel);
        Assert.Equal("Zeitüberschreitung", Record("u", null, RequestOutcome.Timeout).StatusLabel);
        Assert.Equal("abgebrochen", Record("u", null, RequestOutcome.Cancelled).StatusLabel);
        Assert.Equal("keine Verbindung", Record("u", null, RequestOutcome.NetworkError).StatusLabel);
    }
}

public class ConnectivityCheckTests
{
    [Fact]
    public void Endpoints_CoverEveryServiceTheApplicationUses()
    {
        var hosts = ConnectivityCheck.Endpoints
            .Select(e => new Uri(e.Url).Host)
            .ToList();

        Assert.Contains("api.open-meteo.com", hosts);
        Assert.Contains("api.brightsky.dev", hosts);
        Assert.Contains("maps.dwd.de", hosts);
        Assert.Contains("api.rainviewer.com", hosts);
        Assert.Contains("tile.openstreetmap.org", hosts);
    }

    [Fact]
    public void Endpoints_AreAllAbsoluteHttpsUrls()
    {
        Assert.All(ConnectivityCheck.Endpoints, e =>
        {
            Assert.True(Uri.TryCreate(e.Url, UriKind.Absolute, out Uri? uri));
            Assert.Equal("https", uri!.Scheme);
        });
    }

    [Fact]
    public void Endpoints_AllCarryAGermanName()
    {
        Assert.All(ConnectivityCheck.Endpoints, e => Assert.False(string.IsNullOrWhiteSpace(e.Name)));
    }

    private static EndpointStatus Status(bool reachable) =>
        new("X", "https://x.example", reachable, reachable ? 200 : null, TimeSpan.Zero, "d");

    [Fact]
    public void Summarise_NamesTheTotalOutageCase()
    {
        string text = ConnectivityCheck.Summarise([Status(false), Status(false)]);

        // A total outage has a different cause than a single failure, and the
        // wording has to point at the network rather than at a layer name.
        Assert.Contains("Kein einziger Dienst erreichbar", text);
        Assert.Contains("Internetverbindung", text);
    }

    [Fact]
    public void Summarise_ReportsFullSuccess()
    {
        Assert.Contains("Alle 2 Dienste erreichbar", ConnectivityCheck.Summarise([Status(true), Status(true)]));
    }

    [Fact]
    public void Summarise_CountsAPartialOutage()
    {
        string text = ConnectivityCheck.Summarise([Status(true), Status(false), Status(true)]);

        Assert.Contains("2 von 3", text);
    }

    [Fact]
    public void EndpointStatus_LabelsAReachableServiceWithItsTiming()
    {
        var status = new EndpointStatus("X", "u", true, 200, TimeSpan.FromMilliseconds(150), "erreichbar");

        Assert.Contains("200", status.Label);
        Assert.Contains("150", status.Label);
    }

    [Fact]
    public void EndpointStatus_ShowsTheDetailWhenUnreachable()
    {
        var status = new EndpointStatus("X", "u", false, null, TimeSpan.Zero, "Zeitüberschreitung");

        Assert.Equal("Zeitüberschreitung", status.Label);
    }
}
