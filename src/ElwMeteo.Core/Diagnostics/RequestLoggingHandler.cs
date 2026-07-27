using System.Diagnostics;

namespace ElwMeteo.Core.Diagnostics;

/// <summary>
/// Records every outbound HTTP request into a <see cref="RequestLog"/>.
///
/// Sits in the HttpClient pipeline so no call site has to remember to log, and
/// so the log reflects what actually went over the wire — including the query
/// string, which is where most of the interesting mistakes live.
/// </summary>
public sealed class RequestLoggingHandler(RequestLog log) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        string method = request.Method.Method;
        string url = request.RequestUri?.ToString() ?? "—";
        long start = Stopwatch.GetTimestamp();

        try
        {
            HttpResponseMessage response = await base
                .SendAsync(request, cancellationToken)
                .ConfigureAwait(false);

            log.Add(RequestLog.ForResponse(
                method,
                url,
                (int)response.StatusCode,
                Stopwatch.GetElapsedTime(start),
                response.Content.Headers.ContentLength,
                response.ReasonPhrase));

            return response;
        }
        catch (Exception ex)
        {
            log.Add(RequestLog.ForException(
                method,
                url,
                Stopwatch.GetElapsedTime(start),
                ex,
                cancellationToken.IsCancellationRequested));

            throw;
        }
    }
}
