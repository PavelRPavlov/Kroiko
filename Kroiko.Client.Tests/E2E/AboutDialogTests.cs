using System.Text.RegularExpressions;
using Kroiko.Client.Blazor.Updates;
using Microsoft.Playwright;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace Kroiko.Client.Tests.E2E;

/// <summary>
/// The About dialog of the published, trimmed app shows its version, <c>vX.Y.Z (sha)</c>, and checks for a new one
/// (docs/implementation/06-updates-and-about.md, steps 1 and 3; ADR-0002 §4–5).
/// </summary>
[Collection(E2ECollection.Name)]
[Trait("Category", "E2E")]
public sealed partial class AboutDialogTests(PublishedApp app)
{
    [Fact]
    public async Task About_shows_the_version_and_commit_the_app_was_built_from()
    {
        await using var context = await app.NewContextAsync("bg-BG");
        var page = await context.NewPageAsync();
        await page.GotoAsync("/");

        var about = await page.OpenAboutAsync();

        await Expect(about).ToContainTextAsync("Конвертор на списъци за разкрой от Polyboard към бланки за поръчка.");
        // Built from the git repo, Source Link appends the commit, so the sha is there; the published app and the one
        // these tests reference are built from the same commit.
        var version = about.GetByText(VersionAndSha());
        await Expect(version).ToHaveTextAsync(AppVersion.Current);
    }

    [Fact]
    public async Task The_manual_check_finds_the_installed_app_up_to_date()
    {
        await using var context = await app.NewContextAsync("bg-BG");
        var page = await context.NewPageAsync();
        await page.GotoAsync("/");
        await page.WaitForOfflineCacheAsync();

        var about = await page.OpenAboutAsync();
        await about.GetByRole(AriaRole.Button, new() { Name = "Провери за обновления" }).ClickAsync();

        // docs/implementation/06-updates-and-about.md, step 3; the other outcomes: E2E/UpdateOfferTests.
        await Expect(about.GetByRole(AriaRole.Status)).ToHaveTextAsync("Използвате най-новата версия.");
    }

    [GeneratedRegex(@"^v\d+\.\d+\.\d+ \([0-9a-f]{7}\)$")]
    private static partial Regex VersionAndSha();
}
