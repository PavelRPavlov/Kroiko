using Microsoft.Playwright;

namespace Kroiko.Client.Tests.E2E;

/// <summary>Service-worker helpers the offline scenarios share.</summary>
internal static class ServiceWorkerPage
{
    private const int ActivationTimeoutMs = 60_000;

    /// <summary>
    /// Waits until the page's service worker is <c>activated</c>, which it only becomes after its install
    /// cached every precached asset (<c>cache.addAll</c> is all-or-nothing), and returns the number of cached entries.
    /// </summary>
    public static Task<int> WaitForOfflineCacheAsync(this IPage page) =>
        page.EvaluateAsync<int>("""
            async timeoutMs => {
                const activated = (async () => {
                    const worker = (await navigator.serviceWorker.ready).active;
                    if (worker.state !== 'activated')
                        await new Promise(resolve => worker.addEventListener('statechange',
                            () => worker.state === 'activated' && resolve()));
                })();
                const timedOut = new Promise((_, reject) => setTimeout(() => reject(new Error(
                    `The service worker did not activate within ${timeoutMs} ms: its install failed (a precached ` +
                    'asset missing or failing its integrity check) or it never registered.')), timeoutMs));
                await Promise.race([activated, timedOut]);

                const names = (await caches.keys()).filter(name => name.startsWith('offline-cache-'));
                if (names.length !== 1)
                    throw new Error(`Expected one offline cache, found: ${names.join(', ') || 'none'}`);
                return (await (await caches.open(names[0])).keys()).length;
            }
            """, ActivationTimeoutMs);
}
