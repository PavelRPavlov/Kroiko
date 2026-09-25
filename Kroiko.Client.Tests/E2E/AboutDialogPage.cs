using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Kroiko.Client.Tests.E2E;

/// <summary>Opening the About dialog from the app bar in the E2E tests.</summary>
internal static class AboutDialogPage
{
    /// <summary>Clicks "Относно" in the app bar and returns the open About dialog.</summary>
    public static async Task<ILocator> OpenAboutAsync(this IPage page)
    {
        await page.Locator("header.mud-appbar").GetByRole(AriaRole.Button, new() { Name = "Относно" }).ClickAsync();
        var about = page.GetByRole(AriaRole.Dialog).Filter(new() { HasText = "Kroiko" });
        await Expect(about).ToBeVisibleAsync();
        return about;
    }
}
