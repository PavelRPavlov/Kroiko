using FluentAssertions;
using Kroiko.Client.Blazor.Conversion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Xunit;

namespace Kroiko.Client.Tests.Conversion;

/// <summary>
/// The app opens the folder picker, lists the picked folder and writes into it through <c>wwwroot/js/files.js</c>
/// (docs/implementation/05-saving.md, step 2). The JS runtime is faked; the Playwright folder-save tests cover the
/// real browser with the picker stubbed.
/// </summary>
public sealed class FolderPickerRegistrationTests : IAsyncLifetime
{
    private readonly FakeFilesModuleRuntime _js = new();
    private ServiceProvider _provider = null!;
    private AsyncServiceScope _app;

    public Task InitializeAsync()
    {
        var services = new ServiceCollection()
            .AddScoped<IJSRuntime>(_ => _js)
            .AddFileDownloader()
            .AddFolderPicker();
        _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        _app = _provider.CreateAsyncScope();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _app.DisposeAsync();
        await _provider.DisposeAsync();
    }

    private IFolderPicker Picker => _app.ServiceProvider.GetRequiredService<IFolderPicker>();

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_picker_is_available_where_the_files_module_finds_showDirectoryPicker(bool available)
    {
        _js.Module.CanSaveToFolder = available;

        (await Picker.IsAvailableAsync()).Should().Be(available);
    }

    [Fact]
    public async Task A_picked_folder_is_listed_and_written_through_the_files_module_and_released_when_disposed()
    {
        var handle = new FakeFolderHandle();
        _js.Module.PickResult = new() { Outcome = "picked", Name = "Поръчки", Folder = handle };
        _js.Module.Names = ["Бяло ПДЧ.xlsx", "стари"];

        var pick = await Picker.PickAsync();

        pick.Outcome.Should().Be(FolderPickOutcome.Picked);
        await using (var folder = pick.Folder!)
        {
            folder.Name.Should().Be("Поръчки");
            (await folder.ListNamesAsync()).Should().Equal("Бяло ПДЧ.xlsx", "стари");
            await folder.WriteFileAsync("Бяло ПДЧ (2).xlsx", [1, 2, 3]);
            await folder.WriteFileAsync("HDF 3 mm.xlsx", [4]);
            handle.Disposed.Should().BeFalse();
        }

        _js.Module.Listed.Should().Equal(handle);
        _js.Module.Writes.Should().BeEquivalentTo(
            [(handle, "Бяло ПДЧ (2).xlsx", new byte[] { 1, 2, 3 }), (handle, "HDF 3 mm.xlsx", new byte[] { 4 })],
            options => options.WithStrictOrdering());
        handle.Disposed.Should().BeTrue();
        _js.Imports.Should().Equal("./js/files.js");
    }

    [Theory]
    [InlineData("cancelled", FolderPickOutcome.Cancelled)]
    [InlineData("blocked", FolderPickOutcome.Blocked)]
    public async Task A_picker_that_picked_nothing_says_why(string outcome, FolderPickOutcome expected)
    {
        _js.Module.PickResult = new() { Outcome = outcome };

        var pick = await Picker.PickAsync();

        pick.Outcome.Should().Be(expected);
        pick.Folder.Should().BeNull();
    }

    [Fact]
    public async Task An_unknown_pick_outcome_is_an_error()
    {
        _js.Module.PickResult = new() { Outcome = "maybe" };

        var pick = () => Picker.PickAsync();

        await pick.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task The_files_module_is_imported_once_for_downloads_and_folders()
    {
        _js.Module.PickResult = new() { Outcome = "cancelled" };

        await Picker.IsAvailableAsync();
        await Picker.PickAsync();
        await _app.ServiceProvider.GetRequiredService<IFileDownloader>().DownloadAsync("a.xlsx", [1]);

        _js.Imports.Should().Equal("./js/files.js");
    }

    /// <summary>A JS runtime that can only import <c>files.js</c>.</summary>
    private sealed class FakeFilesModuleRuntime : IJSRuntime
    {
        public List<string> Imports { get; } = [];

        public FakeFilesModule Module { get; } = new();

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            if (identifier != "import")
            {
                throw new JSException($"Could not find '{identifier}'.");
            }

            Imports.Add((string)args![0]!);
            return ValueTask.FromResult((TValue)(object)Module);
        }
    }

    /// <summary><c>files.js</c>: answers the folder functions and records what they were handed.</summary>
    private sealed class FakeFilesModule : IJSObjectReference
    {
        public bool CanSaveToFolder { get; set; }

        public BrowserFolderPicker.PickResult PickResult { get; set; } = new() { Outcome = "cancelled" };

        public string[] Names { get; set; } = [];

        public List<IJSObjectReference> Listed { get; } = [];

        public List<(IJSObjectReference Folder, string FileName, byte[] Content)> Writes { get; } = [];

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            object? result;
            switch (identifier)
            {
                case "canSaveToFolder":
                    result = CanSaveToFolder;
                    break;
                case "pickFolder":
                    result = PickResult;
                    break;
                case "listNames":
                    Listed.Add((IJSObjectReference)args![0]!);
                    result = Names;
                    break;
                case "writeFile":
                    // The stream is only readable during the call, as in the browser.
                    var content = new MemoryStream();
                    ((DotNetStreamReference)args![2]!).Stream.CopyTo(content);
                    Writes.Add(((IJSObjectReference)args[0]!, (string)args[1]!, content.ToArray()));
                    result = null;
                    break;
                case "downloadFile":
                    result = null;
                    break;
                default:
                    throw new JSException($"Could not find '{identifier}'.");
            }

            return ValueTask.FromResult((TValue)result!);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>A <c>FileSystemDirectoryHandle</c> as .NET holds it.</summary>
    private sealed class FakeFolderHandle : IJSObjectReference
    {
        public bool Disposed { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            throw new JSException("The handle is only passed back to files.js.");

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            throw new JSException("The handle is only passed back to files.js.");

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
