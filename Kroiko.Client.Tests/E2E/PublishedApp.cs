using System.Diagnostics;
using Microsoft.Playwright;
using Xunit;

namespace Kroiko.Client.Tests.E2E;

/// <summary>
/// The shipped artifact under test (ADR-0007 §5): <c>dotnet publish Kroiko.Client.Blazor -c Release</c>, once per
/// test run, into a temp folder, served by <see cref="StaticSiteHost"/> and driven by the bundled Chromium.
/// Shared by every E2E test through <see cref="E2ECollection"/>; each test opens its own browser context, so
/// service workers and caches never leak between tests.
/// </summary>
public sealed class PublishedApp : IAsyncLifetime
{
    private static readonly TimeSpan PublishTimeout = TimeSpan.FromMinutes(10);
    private const float ExpectTimeoutMs = 15_000; // Expect(...) waits; the offline boot from the cache takes a few seconds.

    private readonly string _publishDir = Directory.CreateTempSubdirectory("kroiko-e2e-").FullName;
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private StaticSiteHost? _host;

    /// <summary>The published app's root, ending in <c>/</c>.</summary>
    public Uri BaseAddress => (_host ?? throw NotStarted()).BaseAddress;

    /// <summary>The published <c>wwwroot</c> on disk: what a deploy uploads and the host serves.</summary>
    public string WebRoot => _host is null ? throw NotStarted() : Path.Combine(_publishDir, "wwwroot");

    public async Task InitializeAsync()
    {
        // The browser first: a missing browser fails in seconds, not after a publish.
        _playwright = await Playwright.CreateAsync();
        _browser = await Chromium.LaunchAsync(_playwright);
        Assertions.SetDefaultExpectTimeout(ExpectTimeoutMs);

        await PublishAsync(_publishDir);
        _host = await StaticSiteHost.StartAsync(Path.Combine(_publishDir, "wwwroot"));
    }

    public async Task DisposeAsync()
    {
        if (_host is not null)
            await _host.DisposeAsync();
        if (_browser is not null)
            await _browser.DisposeAsync();
        _playwright?.Dispose();

        try
        {
            Directory.Delete(_publishDir, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Best effort: a temp folder left behind is harmless.
        }
    }

    /// <summary>A fresh browser context (own service worker, caches and storage) whose relative URLs resolve against the app.</summary>
    public Task<IBrowserContext> NewContextAsync() =>
        (_browser ?? throw NotStarted()).NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = BaseAddress.ToString(),
            ServiceWorkers = ServiceWorkerPolicy.Allow,
        });

    private static async Task PublishAsync(string outputDir)
    {
        var project = RepoPaths.ClientProject;
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            // No build servers: they would outlive the publish holding its output pipes open.
            ArgumentList = { "publish", project, "-c", "Release", "-o", outputDir, "--nologo", "--disable-build-servers" },
        };

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start dotnet publish.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(PublishTimeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw new TimeoutException(
                $"dotnet publish of {project} did not finish within {PublishTimeout}:{Environment.NewLine}{await stdout}{await stderr}");
        }

        var output = await stdout + await stderr;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"dotnet publish of {project} failed with exit code {process.ExitCode}:{Environment.NewLine}{output}");
        }
    }

    private static InvalidOperationException NotStarted() => new("The published app has not started.");
}
