using FluentAssertions;
using Microsoft.Playwright;
using Xunit;
using static Kroiko.Client.Tests.E2E.ConverterPage;
using static Microsoft.Playwright.Assertions;

namespace Kroiko.Client.Tests.E2E;

/// <summary>
/// The published app starts fully offline once its service worker has cached it: no failed requests and
/// Roboto included (docs/implementation/03-client-shell.md, step 3), and converts an Order offline
/// (docs/implementation/04-conversion-flow.md, step 6).
/// </summary>
[Collection(E2ECollection.Name)]
[Trait("Category", "E2E")]
public sealed class OfflineShellTests(PublishedApp app)
{
    [Fact]
    public async Task Starts_offline_with_the_app_bar_the_configuration_page_and_Roboto()
    {
        await using var context = await app.NewContextAsync();
        var requests = new RequestLog(context, app.BaseAddress);
        var page = await context.NewPageAsync();

        await StartOfflineAsync(context, page, requests);

        var appBar = page.Locator("header.mud-appbar");
        await Expect(appBar.GetByRole(AriaRole.Img, new() { Name = "Kroiko" })).ToBeVisibleAsync();

        await appBar.GetByRole(AriaRole.Link, new() { Name = "Configuration", Exact = true }).ClickAsync();
        await Expect(page).ToHaveURLAsync(new Uri(app.BaseAddress, "configuration").ToString());
        await Expect(page.GetByText("Cutting Lists Options...")).ToBeVisibleAsync();

        // A deep link, loaded offline: the service worker answers the navigation with the cached index.html.
        await page.GotoAsync("configuration");
        await Expect(page.GetByText("Cutting Lists Options...")).ToBeVisibleAsync();

        // load() fetches the regular face through the service worker; offline it only succeeds from the cache.
        var fontLoad = await page.EvaluateAsync<string>(
            "() => document.fonts.load('16px Roboto').then(() => 'loaded', error => `${error.name}: ${error.message}`)");

        requests.Failed.Should().BeEmpty("the app must start offline without a failed request");
        requests.CrossOrigin.Should().BeEmpty("the app makes no cross-origin requests");
        fontLoad.Should().Be("loaded");
        (await page.EvaluateAsync<bool>("() => document.fonts.check('16px Roboto')")).Should().BeTrue();
        (await page.EvaluateAsync<string[]>(
                "() => [...document.fonts].filter(f => f.family.replace(/[\"']/g, '') === 'Roboto' && f.status === 'loaded').map(f => f.weight)"))
            .Should().Contain("400", "the regular Roboto face must load from the offline cache");
    }

    [Fact]
    public async Task After_an_offline_start_a_Lonira_conversion_matches_the_golden_files()
    {
        await using var context = await app.NewContextAsync("bg-BG");
        var requests = new RequestLog(context, app.BaseAddress);
        var page = await context.NewPageAsync();
        var console = ConsoleErrors(page);

        await StartOfflineAsync(context, page, requests);

        // The device remembers no manufacturer, so the picker starts on Lonira.
        var files = await ConvertAsGoldenAsync(page, "wardrobes-4-materials");

        MatchGolden("wardrobes-4-materials", "Lonira", files);
        requests.Failed.Should().BeEmpty("the conversion needs nothing the offline cache lacks");
        console.Should().BeEmpty();
    }

    // Loads the app, waits until its service worker has cached it, goes offline and reloads; failed requests are
    // recorded from then on.
    private static async Task StartOfflineAsync(IBrowserContext context, IPage page, RequestLog requests)
    {
        await page.GotoAsync("/");
        (await page.WaitForOfflineCacheAsync()).Should().BePositive();

        await context.SetOfflineAsync(true);
        requests.StartRecordingFailures();
        await page.ReloadAsync();
    }
}
