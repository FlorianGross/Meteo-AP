using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

namespace ElwMeteo.Core.Diagnostics;

public enum RequestOutcome
{
    Success,
    HttpError,
    NetworkError,
    Timeout,
    Cancelled
}

/// <summary>One outbound request, as it actually happened.</summary>
public sealed record RequestRecord(
    DateTimeOffset Timestamp,
    string Method,
    string Url,
    int? StatusCode,
    RequestOutcome Outcome,
    TimeSpan Duration,
    long? ContentLength,
    string? Detail)
{
    public bool IsFailure => Outcome != RequestOutcome.Success;

    /// <summary>Host only, for grouping a log by service.</summary>
    public string Host => Uri.TryCreate(Url, UriKind.Absolute, out Uri? uri) ? uri.Host : "—";

    public string StatusLabel => Outcome switch
    {
        RequestOutcome.Success => $"{StatusCode} OK",
        RequestOutcome.HttpError => $"{StatusCode} Fehler",
        RequestOutcome.Timeout => "Zeitüberschreitung",
        RequestOutcome.Cancelled => "abgebrochen",
        _ => "keine Verbindung"
    };

    public string DurationLabel => $"{Duration.TotalMilliseconds:F0} ms";
}

/// <summary>
/// A bounded, thread-safe record of the application's outbound requests.
///
/// The reason this exists: every remote source here can fail in a way that looks
/// identical from the outside — an empty overlay. Without the actual URL and
/// status code, diagnosing it is guesswork. With them it is a two-minute job.
/// </summary>
public sealed class RequestLog(int capacity = 200)
{
    private readonly ConcurrentQueue<RequestRecord> _records = new();

    public int Capacity { get; } = Math.Max(10, capacity);

    /// <summary>Raised after each entry, so the view can refresh.</summary>
    public event Action<RequestRecord>? Recorded;

    public void Add(RequestRecord record)
    {
        _records.Enqueue(record);

        // Keep only the most recent entries; a long shift must not grow unbounded.
        while (_records.Count > Capacity && _records.TryDequeue(out _))
        {
        }

        Recorded?.Invoke(record);
    }

    /// <summary>Newest first, which is the order anyone reads a log in.</summary>
    public IReadOnlyList<RequestRecord> Snapshot() =>
        _records.Reverse().ToList();

    public void Clear()
    {
        while (_records.TryDequeue(out _))
        {
        }
    }

    /// <summary>Failures grouped by host, with a count — the summary worth reading first.</summary>
    public IReadOnlyList<(string Host, int Failures, int Total)> HostSummary()
    {
        return _records
            .GroupBy(r => r.Host)
            .Select(g => (Host: g.Key, Failures: g.Count(r => r.IsFailure), Total: g.Count()))
            .OrderByDescending(entry => entry.Failures)
            .ThenBy(entry => entry.Host, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// The whole log as plain text, ready to paste into a bug report. Deliberately
    /// includes the full URL: without it there is nothing to reproduce.
    /// </summary>
    public string ToPlainText(DateTimeOffset now)
    {
        var text = new StringBuilder();

        text.AppendLine($"ELW-Meteo Verbindungsprotokoll  {now.ToLocalTime():dd.MM.yyyy HH:mm:ss}");
        text.AppendLine(new string('=', 70));
        text.AppendLine();

        var summary = HostSummary();
        if (summary.Count > 0)
        {
            text.AppendLine("ÜBERSICHT");
            foreach ((string host, int failures, int total) in summary)
            {
                text.AppendLine($"  {host,-42} {total,4} Anfragen, {failures,4} Fehler");
            }

            text.AppendLine();
        }

        text.AppendLine("EINZELNE ANFRAGEN (neueste zuerst)");

        foreach (RequestRecord record in Snapshot())
        {
            text.AppendLine(
                $"  {record.Timestamp.ToLocalTime():HH:mm:ss}  " +
                $"{record.StatusLabel,-22} {record.DurationLabel,8}  {record.Method} {record.Url}");

            if (!string.IsNullOrWhiteSpace(record.Detail))
            {
                text.AppendLine($"        → {record.Detail}");
            }
        }

        if (_records.IsEmpty)
        {
            text.AppendLine("  (noch keine Anfragen)");
        }

        return text.ToString();
    }

    /// <summary>Builds a record from a completed request.</summary>
    public static RequestRecord ForResponse(
        string method,
        string url,
        int statusCode,
        TimeSpan duration,
        long? contentLength,
        string? reasonPhrase)
    {
        bool success = statusCode is >= 200 and < 400;

        return new RequestRecord(
            DateTimeOffset.UtcNow,
            method,
            url,
            statusCode,
            success ? RequestOutcome.Success : RequestOutcome.HttpError,
            duration,
            contentLength,
            success ? null : reasonPhrase);
    }

    /// <summary>Builds a record from a request that never produced a response.</summary>
    public static RequestRecord ForException(
        string method,
        string url,
        TimeSpan duration,
        Exception exception,
        bool wasCancelledByCaller)
    {
        RequestOutcome outcome = exception switch
        {
            TaskCanceledException when wasCancelledByCaller => RequestOutcome.Cancelled,
            TaskCanceledException or TimeoutException => RequestOutcome.Timeout,
            _ => RequestOutcome.NetworkError
        };

        // The innermost message is the one that names the real cause, e.g. a
        // DNS failure or a rejected certificate behind a corporate proxy.
        Exception innermost = exception;
        while (innermost.InnerException is not null)
        {
            innermost = innermost.InnerException;
        }

        return new RequestRecord(
            DateTimeOffset.UtcNow,
            method,
            url,
            null,
            outcome,
            duration,
            null,
            innermost.Message);
    }

    /// <summary>Records something that failed outside HttpClient, e.g. a map tile.</summary>
    public void AddExternalFailure(string source, string detail) =>
        Add(new RequestRecord(
            DateTimeOffset.UtcNow,
            "MAP",
            source,
            null,
            RequestOutcome.NetworkError,
            TimeSpan.Zero,
            null,
            detail));

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"RequestLog({_records.Count}/{Capacity})");
}
