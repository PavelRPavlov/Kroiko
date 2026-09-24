using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace Kroiko.Client.Blazor.Updates;

/// <summary>
/// New versions of the installed app (ADR-0002 §1, §3–4), over <c>wwwroot/js/updates.js</c>, which
/// <c>index.html</c> starts with the service worker's registration. The browser downloads a new version in the
/// background; once it waits, <see cref="IsUpdateReady"/> turns true and <see cref="UpdateReady"/> is raised, once.
/// <see cref="ApplyUpdateAsync"/> activates it and reloads the page; <see cref="CheckNowAsync"/> is the manual check.
/// Only a published build has the service worker that applies a version. A failing JS call is logged, not thrown: the
/// next launch applies a waiting version anyway.
/// </summary>
public sealed class AppUpdates(IJSRuntime js, ILogger<AppUpdates> logger) : IAsyncDisposable
{
    private const string ModulePath = "./js/updates.js";

    private Task<IJSObjectReference>? _module;
    private DotNetObjectReference<Listener>? _listener;

    /// <summary>A new version has been downloaded completely and waits to be applied.</summary>
    public bool IsUpdateReady { get; private set; }

    /// <summary>
    /// <see cref="IsUpdateReady"/> turned true. Raised once, from a JS callback, which WebAssembly runs on its one thread;
    /// the app hears it through <see cref="UpdateOffer.Changed"/>.
    /// </summary>
    public event Action? UpdateReady;

    /// <summary>
    /// Subscribes to <c>updates.js</c>, which then reports a version that waits now or later. Starting again does
    /// nothing, unless the last start failed.
    /// </summary>
    public async Task StartAsync()
    {
        if (_listener is not null)
            return;

        _listener = DotNetObjectReference.Create(new Listener(this));
        try
        {
            var module = await ModuleAsync();
            await module.InvokeVoidAsync("subscribe", _listener);
        }
        catch (JSException exception)
        {
            logger.LogWarning(exception, "The app could not watch for new versions.");
            _listener.Dispose();
            _listener = null;
        }
    }

    /// <summary>
    /// Asks the server for a newer version now (ADR-0002 §4). <see cref="UpdateCheck.Offline"/> also stands for a
    /// check that failed.
    /// </summary>
    public async Task<UpdateCheck> CheckNowAsync()
    {
        try
        {
            var module = await ModuleAsync();
            var outcome = await module.InvokeAsync<string>("checkNow");
            switch (outcome)
            {
                case "upToDate":
                    return UpdateCheck.UpToDate;
                case "downloading":
                    return UpdateCheck.Downloading;
                case "offline":
                    return UpdateCheck.Offline;
                default:
                    logger.LogWarning("The check for a new version answered {Outcome}.", outcome);
                    return UpdateCheck.Offline;
            }
        }
        catch (JSException exception)
        {
            logger.LogWarning(exception, "The check for a new version failed.");
            return UpdateCheck.Offline;
        }
    }

    /// <summary>
    /// Activates the waiting version; the page reloads once it has taken over (ADR-0002 §1). The caller asks first
    /// when the Order has unsaved work (ADR-0002 §2). <c>false</c> when it could not be applied, or did not take over
    /// within <c>updates.js</c>' timeout, and the page stays.
    /// </summary>
    public async Task<bool> ApplyUpdateAsync()
    {
        try
        {
            var module = await ModuleAsync();
            await module.InvokeVoidAsync("applyUpdate");
            return true;
        }
        catch (JSException exception)
        {
            logger.LogError(exception, "The new version could not be applied.");
            return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        var module = _module is { IsCompletedSuccessfully: true } ? _module.Result : null;
        _module = null;
        try
        {
            if (module is not null)
            {
                if (_listener is not null)
                    await module.InvokeVoidAsync("unsubscribe");
                await module.DisposeAsync();
            }
        }
        catch (Exception exception) when (exception is JSException or JSDisconnectedException)
        {
            // The page is gone, and the module with it.
        }
        finally
        {
            _listener?.Dispose();
            _listener = null;
        }
    }

    private async Task<IJSObjectReference> ModuleAsync()
    {
        try
        {
            return await (_module ??= js.InvokeAsync<IJSObjectReference>("import", ModulePath).AsTask());
        }
        catch
        {
            // Import again next time rather than keep a failed import.
            _module = null;
            throw;
        }
    }

    private void OnUpdateReady()
    {
        if (IsUpdateReady)
            return;

        logger.LogInformation("A new version has been downloaded and waits to be applied.");
        IsUpdateReady = true;
        UpdateReady?.Invoke();
    }

    /// <summary>What <c>updates.js</c> calls back, kept off <see cref="AppUpdates"/>' own API.</summary>
    private sealed class Listener(AppUpdates updates)
    {
        [JSInvokable]
        public void OnUpdateReady() => updates.OnUpdateReady();
    }
}
