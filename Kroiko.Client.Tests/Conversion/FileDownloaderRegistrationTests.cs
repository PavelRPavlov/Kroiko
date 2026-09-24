using FluentAssertions;
using Kroiko.Client.Blazor.Conversion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Xunit;

namespace Kroiko.Client.Tests.Conversion;

/// <summary>
/// The app hands downloads to <c>wwwroot/js/files.js</c> (docs/implementation/04-conversion-flow.md, step 5). The JS
/// runtime is faked; the Playwright generation tests cover the real browser.
/// </summary>
public sealed class FileDownloaderRegistrationTests
{
    [Fact]
    public async Task The_app_streams_each_download_to_the_files_module()
    {
        var js = new FakeFilesModuleRuntime();
        var services = new ServiceCollection()
            .AddScoped<IJSRuntime>(_ => js)
            .AddFileDownloader();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        await using var app = provider.CreateAsyncScope();
        var downloader = app.ServiceProvider.GetRequiredService<IFileDownloader>();

        await downloader.DownloadAsync("Бяло ПДЧ.xlsx", [1, 2, 3]);
        await downloader.DownloadAsync("HDF 3 mm.xlsx", [4]);

        js.Imports.Should().Equal("./js/files.js");
        js.Module.Downloads.Should().BeEquivalentTo(
            [("Бяло ПДЧ.xlsx", new byte[] { 1, 2, 3 }), ("HDF 3 mm.xlsx", new byte[] { 4 })],
            options => options.WithStrictOrdering());
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

    /// <summary><c>files.js</c>: records what <c>downloadFile</c> was handed.</summary>
    private sealed class FakeFilesModule : IJSObjectReference
    {
        public List<(string FileName, byte[] Content)> Downloads { get; } = [];

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            if (identifier != "downloadFile")
            {
                throw new JSException($"Could not find '{identifier}'.");
            }

            // The stream is only readable during the call, as in the browser.
            var content = new MemoryStream();
            ((DotNetStreamReference)args![1]!).Stream.CopyTo(content);
            Downloads.Add(((string)args[0]!, content.ToArray()));
            return ValueTask.FromResult(default(TValue)!);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
