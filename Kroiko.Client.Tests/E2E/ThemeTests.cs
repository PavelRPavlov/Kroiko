using FluentAssertions;
using Microsoft.Playwright;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace Kroiko.Client.Tests.E2E;

/// <summary>
/// The theme picker in the app bar of the published app (docs/implementation/08-theme.md, step 3; ADR-0014): each pick
/// applies at once, „Системна“ follows the device live, and the pick is where the next visit starts — the loading
/// screen included. The device's setting is Chromium's emulated <c>prefers-color-scheme</c>.
/// </summary>
[Collection(E2ECollection.Name)]
[Trait("Category", "E2E")]
public sealed class ThemeTests(PublishedApp app)
{
    // The body's background: MudBlazor's default light and dark palettes (ADR-0014 §4).
    private const string LightBackground = "rgb(255, 255, 255)";
    private const string DarkBackground = "rgb(50, 51, 61)";

    // Records <html data-theme> once the page's own scripts have run, before Blazor has started.
    private const string RecordBootTheme =
        "document.addEventListener('DOMContentLoaded', () => { window.bootTheme = document.documentElement.getAttribute('data-theme'); });";

    [Fact]
    public async Task Each_pick_applies_at_once_and_the_last_one_starts_the_next_visit()
    {
        await using var context = await app.NewContextAsync("bg-BG");
        await context.AddInitScriptAsync(RecordBootTheme);
        var page = await context.NewPageAsync();
        await page.EmulateMediaAsync(new() { ColorScheme = ColorScheme.Dark });
        await page.GotoAsync("/");

        // „Системна“ follows the device, and keeps following it.
        await PickAsync(page, "Системна");
        await ExpectThemeAsync(page, "Системна", dark: true);
        await page.EmulateMediaAsync(new() { ColorScheme = ColorScheme.Light });
        await ExpectThemeAsync(page, "Системна", dark: false);

        // A pick overrides the device: „Тъмна“ on a light one, „Светла“ on a dark one.
        await PickAsync(page, "Тъмна");
        await ExpectThemeAsync(page, "Тъмна", dark: true);
        await page.EmulateMediaAsync(new() { ColorScheme = ColorScheme.Dark });
        await PickAsync(page, "Светла");
        await ExpectThemeAsync(page, "Светла", dark: false);

        // The next visit, on the dark device, starts in the pick from before Blazor boots.
        await page.ReloadAsync();
        await ExpectThemeAsync(page, "Светла", dark: false);
        (await page.EvaluateAsync<string?>("window.bootTheme")).Should().Be("light");
    }

    private static ILocator ThemeButton(IPage page) =>
        page.Locator("header.mud-appbar").GetByRole(AriaRole.Button, new() { Name = "Тема" });

    private static async Task PickAsync(IPage page, string choice)
    {
        await ThemeButton(page).ClickAsync();
        await page.GetByRole(AriaRole.Menuitemradio, new() { Name = choice }).ClickAsync();
    }

    // The app shows the palette, <html> carries it for the page around the app, and the menu marks the choice.
    private static async Task ExpectThemeAsync(IPage page, string choice, bool dark)
    {
        await Expect(page.Locator("body")).ToHaveCSSAsync("background-color", dark ? DarkBackground : LightBackground);
        await Expect(page.Locator("html")).ToHaveAttributeAsync("data-theme", dark ? "dark" : "light");

        await ThemeButton(page).ClickAsync();
        var marked = page.GetByRole(AriaRole.Menuitemradio, new() { Checked = true });
        await Expect(marked).ToHaveCountAsync(1);
        await Expect(marked).ToHaveTextAsync(choice);
        // Closed by a click beside it, on the empty bottom-left corner of the page.
        await page.Mouse.ClickAsync(5, page.ViewportSize!.Height - 5);
        await Expect(marked).ToBeHiddenAsync();
    }
}
