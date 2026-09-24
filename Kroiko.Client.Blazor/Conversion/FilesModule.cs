using Microsoft.JSInterop;

namespace Kroiko.Client.Blazor.Conversion;

/// <summary>
/// <c>wwwroot/js/files.js</c>, imported once for the app and shared by the downloads (<see cref="BrowserFileDownloader"/>)
/// and the folder picker (<see cref="BrowserFolderPicker"/>). A failed import is tried again on the next call.
/// </summary>
internal sealed class FilesModule(IJSRuntime js) : IAsyncDisposable
{
    private Task<IJSObjectReference>? _module;

    public async Task<IJSObjectReference> GetAsync()
    {
        try
        {
            return await (_module ??= js.InvokeAsync<IJSObjectReference>("import", "./js/files.js").AsTask());
        }
        catch
        {
            // Import again next time rather than keep a failed import.
            _module = null;
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_module is { IsCompletedSuccessfully: true })
        {
            try
            {
                await (await _module).DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
                // The page is gone, and the module with it.
            }
        }
    }
}
