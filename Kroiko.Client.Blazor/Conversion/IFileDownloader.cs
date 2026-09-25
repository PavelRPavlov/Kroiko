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
internal sealed class BrowserFileDownloader(FilesModule files) : IFileDownloader
{
    public async Task DownloadAsync(string fileName, byte[] content)
    {
        var module = await files.GetAsync();
        using var stream = new MemoryStream(content, writable: false);
        using var streamReference = new DotNetStreamReference(stream);
        await module.InvokeVoidAsync("downloadFile", fileName, streamReference);
    }
}
