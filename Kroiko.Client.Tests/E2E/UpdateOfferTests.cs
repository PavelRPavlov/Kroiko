using FluentAssertions;
using Microsoft.Playwright;
using Xunit;
using static Kroiko.Client.Tests.E2E.ConverterPage;
using static Microsoft.Playwright.Assertions;

namespace Kroiko.Client.Tests.E2E;

/// <summary>
/// The reload snackbar and the update controls in About, in the published app
/// (docs/implementation/06-updates-and-about.md, step 3; ADR-0002 §1–4). A real new version needs a second build, which
/// is checked by hand (ADR-0007 §5, alternatives), so here the page gets a stand-in <c>updates.js</c> that announces a
/// waiting version on request and records <c>applyUpdate</c>: the app's side, from the interop callback to the buttons,
/// is the real one. <c>UpdatesModuleTests</c> and <c>AboutDialogTests</c> cover the real module.
/// </summary>
[Collection(E2ECollection.Name)]
[Trait("Category", "E2E")]
public sealed class UpdateOfferTests(PublishedApp app)
{
    // Served to the page instead of js/updates.js. Only the page's own requests are routed: the service worker still
    // precaches the real module, whose integrity it checks. The page is not controlled by it on its first load.
    private const string StandInModule = """
        let subscriber = null;
        const stand = window.updatesStandIn = {
            applied: 0,
            checkOutcome: 'upToDate',
            subscribed: () => subscriber !== null,
            announce: () => subscriber.invokeMethodAsync('OnUpdateReady'),
        };
        export function start() {}
        export function subscribe(listener) { subscriber = listener; }
        export function unsubscribe() { subscriber = null; }
        export async function applyUpdate() { stand.applied++; }
        export async function checkNow() { return stand.checkOutcome; }
        """;

    [Fact]
    public async Task A_waiting_version_shows_the_snackbar_and_Reload_applies_it()
    {
        var (context, page, console) = await OpenAsync();
        await using var _ = context;

        await AnnounceNewVersionAsync(page);

        var snackbar = UpdateSnackbar(page);
        await Expect(snackbar).ToContainTextAsync("Нова версия е налична");
        await snackbar.GetByRole(AriaRole.Button, new() { Name = "Презареди" }).ClickAsync();

        await ExpectAppliedAsync(page, 1);
        await Expect(page.GetByRole(AriaRole.Dialog)).ToHaveCountAsync(0);
        console.Should().BeEmpty();
    }

    [Fact]
    public async Task Reload_over_an_unsaved_Order_asks_first_and_no_keeps_the_Order()
    {
        var (context, page, console) = await OpenAsync();
        await using var _ = context;
        await UploadAsync(page, "wardrobes-4-materials");
        await Expect(page.GetByLabel("Име на клиента")).ToBeVisibleAsync();
        await AnnounceNewVersionAsync(page);
        var snackbar = UpdateSnackbar(page);
        var reload = snackbar.GetByRole(AriaRole.Button, new() { Name = "Презареди" });

        await reload.ClickAsync();
        var dialog = page.GetByRole(AriaRole.Dialog);
        await Expect(dialog).ToContainTextAsync("Текущата поръчка ще бъде изгубена. Да презаредя ли с новата версия?");
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Не" }).ClickAsync();

        await Expect(dialog).ToHaveCountAsync(0);
        await ExpectAppliedAsync(page, 0);
        await Expect(page.GetByLabel("Име на клиента")).ToBeVisibleAsync();
        await Expect(snackbar).ToBeVisibleAsync();

        await reload.ClickAsync();
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Да" }).ClickAsync();

        await ExpectAppliedAsync(page, 1);
        console.Should().BeEmpty();
    }

    [Fact]
    public async Task Later_hides_the_snackbar_and_About_offers_the_reload()
    {
        var (context, page, console) = await OpenAsync();
        await using var _ = context;
        await AnnounceNewVersionAsync(page);
        var snackbar = UpdateSnackbar(page);

        await snackbar.GetByRole(AriaRole.Button, new() { Name = "По-късно" }).ClickAsync();

        await Expect(snackbar).ToHaveCountAsync(0);
        var about = await OpenAboutAsync(page);
        await Expect(about).ToContainTextAsync("Нова версия е налична");
        await Expect(about.GetByRole(AriaRole.Button, new() { Name = "Провери за обновления" })).ToHaveCountAsync(0);
        await about.GetByRole(AriaRole.Button, new() { Name = "Презареди" }).ClickAsync();

        await ExpectAppliedAsync(page, 1);
        console.Should().BeEmpty();
    }

    [Fact]
    public async Task About_reports_each_outcome_of_the_manual_check()
    {
        var (context, page, console) = await OpenAsync();
        await using var _ = context;
        var about = await OpenAboutAsync(page);
        var check = about.GetByRole(AriaRole.Button, new() { Name = "Провери за обновления" });
        var report = about.GetByRole(AriaRole.Status);

        foreach (var (outcome, text) in new[]
                 {
                     ("upToDate", "Използвате най-новата версия."),
                     ("downloading", "Изтегля се нова версия. Ще можете да презаредите, щом е готова."),
                     ("offline", "Няма връзка със сървъра. Опитайте отново по-късно."),
                 })
        {
            await page.EvaluateAsync("outcome => window.updatesStandIn.checkOutcome = outcome", outcome);
            await check.ClickAsync();
            await Expect(report).ToHaveTextAsync(text);
        }

        // The downloaded version then waits: About offers it in place of the check.
        await AnnounceNewVersionAsync(page);
        await Expect(about.GetByRole(AriaRole.Button, new() { Name = "Презареди" })).ToBeVisibleAsync();
        await Expect(check).ToHaveCountAsync(0);
        console.Should().BeEmpty();
    }

    private async Task<(IBrowserContext Context, IPage Page, IReadOnlyCollection<string> Console)> OpenAsync()
    {
        var context = await app.NewContextAsync("bg-BG");
        var page = await context.NewPageAsync();
        var console = ConsoleErrors(page);
        await page.RouteAsync("**/js/updates.js", route => route.FulfillAsync(new()
        {
            ContentType = "text/javascript",
            Body = StandInModule,
        }));
        await page.GotoAsync("/");
        // MainLayout subscribes on its first render.
        await page.WaitForFunctionAsync("() => window.updatesStandIn?.subscribed()");
        return (context, page, console);
    }

    private static Task AnnounceNewVersionAsync(IPage page) => page.EvaluateAsync("() => window.updatesStandIn.announce()");

    private static ILocator UpdateSnackbar(IPage page) =>
        page.Locator(".mud-snackbar").Filter(new() { HasText = "Нова версия е налична" });

    private static async Task<ILocator> OpenAboutAsync(IPage page)
    {
        await page.Locator("header.mud-appbar").GetByRole(AriaRole.Button, new() { Name = "Относно" }).ClickAsync();
        var about = page.GetByRole(AriaRole.Dialog);
        await Expect(about).ToContainTextAsync("Kroiko");
        return about;
    }

    // Waits for the count to be reached, then checks it was not exceeded (0: nothing was applied by now).
    private static async Task ExpectAppliedAsync(IPage page, int times)
    {
        await page.WaitForFunctionAsync("times => window.updatesStandIn.applied >= times", times);
        (await page.EvaluateAsync<int>("() => window.updatesStandIn.applied")).Should().Be(times);
    }
}
