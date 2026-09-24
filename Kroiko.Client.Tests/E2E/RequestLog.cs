using System.Collections.Concurrent;
using Microsoft.Playwright;

namespace Kroiko.Client.Tests.E2E;

/// <summary>
/// Records, for a browser context, the requests that failed (network errors and HTTP errors) and the requests
/// that left the app's origin.
/// </summary>
internal sealed class RequestLog
{
    private readonly ConcurrentQueue<string> _failed = new();
    private readonly ConcurrentQueue<string> _crossOrigin = new();
    private volatile bool _recordingFailures;

    public RequestLog(IBrowserContext context, Uri appOrigin)
    {
        context.Request += (_, request) =>
        {
            if (Uri.TryCreate(request.Url, UriKind.Absolute, out var url)
                && url.Scheme is "http" or "https"
                && Uri.Compare(url, appOrigin, UriComponents.SchemeAndServer, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) != 0)
            {
                _crossOrigin.Enqueue(request.Url);
            }
        };
        context.RequestFailed += (_, request) =>
        {
            if (_recordingFailures)
                _failed.Enqueue($"{request.Method} {request.Url}: {request.Failure}");
        };
        context.Response += (_, response) =>
        {
            if (_recordingFailures && response.Status >= 400)
                _failed.Enqueue($"{response.Request.Method} {response.Url}: HTTP {response.Status}");
        };
    }

    /// <summary>Requests that failed since <see cref="StartRecordingFailures"/>.</summary>
    public IReadOnlyCollection<string> Failed => _failed.ToArray();

    /// <summary>Every http(s) request to another origin, from the start.</summary>
    public IReadOnlyCollection<string> CrossOrigin => _crossOrigin.ToArray();

    public void StartRecordingFailures() => _recordingFailures = true;
}
