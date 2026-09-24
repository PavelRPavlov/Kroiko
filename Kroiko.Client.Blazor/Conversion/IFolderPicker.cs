using Microsoft.JSInterop;

namespace Kroiko.Client.Blazor.Conversion;

/// <summary>
/// The browser's folder picker (<c>showDirectoryPicker</c>, ADR-0003 §2–3, §6). The only interop of "Запази в папка…";
/// which files, under which names and what that means for the Order is <see cref="ConverterState"/>'s, which the tests
/// exercise with a fake.
/// </summary>
public interface IFolderPicker
{
    /// <summary>The browser can pick a folder to write into (Chrome and Edge; not Firefox or Safari).</summary>
    Task<bool> IsAvailableAsync();

    /// <summary>
    /// Opens the picker, with write access, at the folder picked last on this device. It needs the click's user
    /// activation, so the caller awaits nothing before it.
    /// </summary>
    Task<FolderPick> PickAsync();
}

/// <summary>A folder the operator picked, with write access; disposing it releases the handle.</summary>
public interface IPickedFolder : IAsyncDisposable
{
    /// <summary>The folder's name, as the picker showed it.</summary>
    string Name { get; }

    /// <summary>The name of every entry in the folder, files and subfolders alike.</summary>
    Task<IReadOnlyList<string>> ListNamesAsync();

    /// <summary>Writes <paramref name="content"/> as <paramref name="fileName"/>, used as given.</summary>
    Task WriteFileAsync(string fileName, byte[] content);
}

/// <summary>How opening the folder picker ended.</summary>
public enum FolderPickOutcome
{
    /// <summary>The operator picked a folder and allowed writing into it.</summary>
    Picked,

    /// <summary>The operator closed the picker or did not allow writing (<c>AbortError</c>).</summary>
    Cancelled,

    /// <summary>The browser refused to open the picker (<c>SecurityError</c> / <c>NotAllowedError</c>, e.g. an Edge policy).</summary>
    Blocked,
}

/// <summary>
/// The result of <see cref="IFolderPicker.PickAsync"/>: the <see cref="Folder"/> exactly when it was
/// <see cref="FolderPickOutcome.Picked"/>.
/// </summary>
public sealed class FolderPick
{
    private FolderPick(FolderPickOutcome outcome, IPickedFolder? folder)
    {
        Outcome = outcome;
        Folder = folder;
    }

    public static FolderPick Cancelled { get; } = new(FolderPickOutcome.Cancelled, null);

    public static FolderPick Blocked { get; } = new(FolderPickOutcome.Blocked, null);

    public FolderPickOutcome Outcome { get; }

    public IPickedFolder? Folder { get; }

    public static FolderPick Picked(IPickedFolder folder) =>
        new(FolderPickOutcome.Picked, folder ?? throw new ArgumentNullException(nameof(folder)));
}

/// <summary>
/// <see cref="IFolderPicker"/> over <c>wwwroot/js/files.js</c> and the File System Access API
/// (<a href="https://wicg.github.io/file-system-access/">WICG</a>): <c>pickFolder</c> opens <c>showDirectoryPicker</c>
/// and hands the picked <c>FileSystemDirectoryHandle</c> to .NET as an <see cref="IJSObjectReference"/>, which
/// <c>listNames</c> and <c>writeFile</c> take back. It throws what the interop throws.
/// </summary>
internal sealed class BrowserFolderPicker(FilesModule files) : IFolderPicker
{
    public async Task<bool> IsAvailableAsync() =>
        await (await files.GetAsync()).InvokeAsync<bool>("canSaveToFolder");

    public async Task<FolderPick> PickAsync()
    {
        var module = await files.GetAsync();
        var result = await module.InvokeAsync<PickResult>("pickFolder");
        return result.Outcome switch
        {
            "picked" when result.Folder is not null => FolderPick.Picked(new PickedFolder(module, result.Name ?? string.Empty, result.Folder)),
            "cancelled" => FolderPick.Cancelled,
            "blocked" => FolderPick.Blocked,
            _ => throw new InvalidOperationException($"files.js pickFolder returned the unknown outcome '{result.Outcome}'."),
        };
    }

    /// <summary>What <c>pickFolder</c> returns: <c>picked</c> with the folder, <c>cancelled</c> or <c>blocked</c>.</summary>
    internal sealed class PickResult
    {
        public string? Outcome { get; set; }

        public string? Name { get; set; }

        public IJSObjectReference? Folder { get; set; }
    }

    private sealed class PickedFolder(IJSObjectReference module, string name, IJSObjectReference handle) : IPickedFolder
    {
        public string Name => name;

        public async Task<IReadOnlyList<string>> ListNamesAsync() =>
            await module.InvokeAsync<string[]>("listNames", handle);

        public async Task WriteFileAsync(string fileName, byte[] content)
        {
            using var stream = new MemoryStream(content, writable: false);
            using var streamReference = new DotNetStreamReference(stream);
            await module.InvokeVoidAsync("writeFile", handle, fileName, streamReference);
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await handle.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
                // The page is gone, and the handle with it.
            }
        }
    }
}
