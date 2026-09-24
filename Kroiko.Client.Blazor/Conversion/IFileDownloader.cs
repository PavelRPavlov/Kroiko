using Microsoft.JSInterop;

namespace Kroiko.Client.Blazor.Conversion;

/// <summary>
/// Hands one file to the browser as a download (ADR-0003 §5). The only interop of "Изтегли всички"; which files,
/// under which names and what that means for the Order is <see cref="ConverterState"/>'s, which the tests exercise
/// with a fake.
/// </summary>
public interface IFileDownloader
{
    /// <summary>Triggers the download of <paramref name="content"/> as <paramref name="fileName"/>, used as given.</summary>
    Task DownloadAsync(string fileName, byte[] content);
}

/// <summary>
/// <see cref="IFileDownloader"/> over <c>wwwroot/js/files.js</c>: the bytes are streamed to the browser through a
/// <see cref="DotNetStreamReference"/> and saved with a <c>Blob</c> and an <c>&lt;a download&gt;</c>. It throws what
/// the interop throws.
/// </summary>
public sealed class BrowserFileDownloader(IJSRuntime js) : IFileDownloader, IAsyncDisposable
{
    private Task<IJSObjectReference>? _module;

    public async Task DownloadAsync(string fileName, byte[] content)
    {
        IJSObjectReference module;
        try
        {
            module = await (_module ??= js.InvokeAsync<IJSObjectReference>("import", "./js/files.js").AsTask());
        }
        catch
        {
            // Import again next time rather than keep a failed import.
            _module = null;
            throw;
        }

        using var stream = new MemoryStream(content, writable: false);
        using var streamReference = new DotNetStreamReference(stream);
        await module.InvokeVoidAsync("downloadFile", fileName, streamReference);
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
