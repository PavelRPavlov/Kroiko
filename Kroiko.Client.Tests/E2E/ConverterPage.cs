using System.Collections.Concurrent;
using Kroiko.Testing;
using Microsoft.Playwright;

namespace Kroiko.Client.Tests.E2E;

/// <summary>Driving the Converter page (<c>/</c>) in the E2E tests.</summary>
internal static class ConverterPage
{
    /// <summary>Uploads a Polyboard fixture from <c>Kroiko.Testing/TestData/polyboard/</c>.</summary>
    public static Task UploadAsync(IPage page, string fixture) =>
        page.Locator("input[type=file]").SetInputFilesAsync(TestData.Polyboard(fixture));

    /// <summary>Uploads a Polyboard file made in the test.</summary>
    public static Task UploadAsync(IPage page, FilePayload file) =>
        page.Locator("input[type=file]").SetInputFilesAsync(file);

    /// <summary>Picks a manufacturer, by its Bulgarian label, in the picker.</summary>
    public static async Task PickAsync(IPage page, string manufacturer)
    {
        await page.Locator(".mud-select").First.ClickAsync();
        await page.GetByRole(AriaRole.Option, new() { Name = manufacturer }).ClickAsync();
    }

    /// <summary>Errors the app logs to the console, e.g. an unhandled exception in a component.</summary>
    public static ConcurrentQueue<string> ConsoleErrors(IPage page)
    {
        var errors = new ConcurrentQueue<string>();
        page.Console += (_, message) =>
        {
            if (message.Type == "error")
                errors.Enqueue(message.Text);
        };
        page.PageError += (_, error) => errors.Enqueue(error);
        return errors;
    }
}
