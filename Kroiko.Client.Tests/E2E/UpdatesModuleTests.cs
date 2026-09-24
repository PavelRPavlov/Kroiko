using FluentAssertions;
using Microsoft.Playwright;
using Xunit;
using static Kroiko.Client.Tests.E2E.ConverterPage;

namespace Kroiko.Client.Tests.E2E;

/// <summary>
/// <c>wwwroot/js/updates.js</c> in the published app, started by <c>index.html</c> with the service worker's
/// registration (docs/implementation/06-updates-and-about.md, step 2; ADR-0002 §4). A new version needs a second
/// build, so the update itself is checked by hand (ADR-0007 §5, alternatives); these tests cover what one build shows.
/// </summary>
[Collection(E2ECollection.Name)]
[Trait("Category", "E2E")]
public sealed class UpdatesModuleTests(PublishedApp app)
{
    // The page imports the module index.html started: one URL, one module instance.
    private const string CheckNow = "async () => (await import('./js/updates.js')).checkNow()";

    [Fact]
    public async Task A_check_of_the_installed_app_finds_it_up_to_date()
    {
        await using var context = await app.NewContextAsync();
        var page = await context.NewPageAsync();
        var console = ConsoleErrors(page);
        await page.GotoAsync("/");
        (await page.WaitForOfflineCacheAsync()).Should().BePositive();

        (await page.EvaluateAsync<string>(CheckNow)).Should().Be("upToDate");
        console.Should().BeEmpty();
    }

    [Fact]
    public async Task Offline_the_checks_are_silent_and_the_manual_check_reports_offline()
    {
        await using var context = await app.NewContextAsync();
        var requests = new RequestLog(context, app.BaseAddress);
        var page = await context.NewPageAsync();
        var console = ConsoleErrors(page);
        await page.GotoAsync("/");
        (await page.WaitForOfflineCacheAsync()).Should().BePositive();

        await context.SetOfflineAsync(true);
        requests.StartRecordingFailures();
        await page.ReloadAsync();
        await page.Locator("header.mud-appbar").WaitForAsync();
        // What starts a background check: the window shown again, the connection back (here, only in name).
        await page.EvaluateAsync("""
            () => {
                document.dispatchEvent(new Event('visibilitychange'));
                window.dispatchEvent(new Event('online'));
            }
            """);

        (await page.EvaluateAsync<string>(CheckNow)).Should().Be("offline");
        requests.Failed.Should().BeEmpty("offline, no update check is attempted");
        console.Should().BeEmpty();
    }
}
