using Kroiko.Client.Blazor.Updates;
using Microsoft.Playwright;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace Kroiko.Client.Tests.E2E;

/// <summary>
/// The About dialog of the published, trimmed app shows its version, <c>vX.Y.Z (sha)</c>
/// (docs/implementation/06-updates-and-about.md, step 1; ADR-0002 §5).
/// </summary>
[Collection(E2ECollection.Name)]
[Trait("Category", "E2E")]
public sealed class AboutDialogTests(PublishedApp app)
{
    [Fact]
    public async Task About_shows_the_version_and_commit_the_app_was_built_from()
    {
        await using var context = await app.NewContextAsync("bg-BG");
        var page = await context.NewPageAsync();
        await page.GotoAsync("/");

        await page.Locator("header.mud-appbar").GetByRole(AriaRole.Button, new() { Name = "Относно" }).ClickAsync();

        // The published app and the one these tests reference are built from the same commit.
        var about = page.GetByRole(AriaRole.Dialog);
        await Expect(about).ToContainTextAsync("Конвертор на списъци за разкрой от Polyboard към бланки за поръчка.");
        await Expect(about.GetByText(AppVersion.Current, new() { Exact = true })).ToBeVisibleAsync();
    }
}
