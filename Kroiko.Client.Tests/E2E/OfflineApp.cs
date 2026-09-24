using System.Collections.Concurrent;
using Microsoft.Playwright;

namespace Kroiko.Client.Tests.E2E;

/// <summary>Service-worker and network helpers the offline scenarios share.</summary>
internal static class OfflineApp
{
    private static readonly TimeSpan ActivationTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Waits until the page's service worker is <c>activated</c>, which it only becomes after its install
    /// cached every precached asset (<c>cache.addAll</c> is all-or-nothing), and returns the number of cached entries.
    /// </summary>
    public static async Task<int> WaitForOfflineCacheAsync(this IPage page)
    {
        const string script = """
            async () => {
                const worker = (await navigator.serviceWorker.ready).active;
                if (worker.state !== 'activated')
                    await new Promise(resolve => worker.addEventListener('statechange',
                        () => worker.state === 'activated' && resolve()));
                const names = (await caches.keys()).filter(name => name.startsWith('offline-cache-'));
                if (names.length !== 1)
                    throw new Error(`Expected one offline cache, found: ${names.join(', ') || 'none'}`);
                return (await (await caches.open(names[0])).keys()).length;
            }
            """;
        try
        {
            return await page.EvaluateAsync<int>(script).WaitAsync(ActivationTimeout);
        }
        catch (TimeoutException e)
        {
            throw new TimeoutException(
                $"The service worker did not activate within {ActivationTimeout.TotalSeconds} s: its install failed " +
                "(a precached asset missing or failing its integrity check) or it never registered.", e);
        }
    }
}

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
