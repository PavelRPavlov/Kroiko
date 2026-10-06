using System.Collections.Concurrent;
using Kroiko.Domain.ExcelFilesGeneration;
using Kroiko.Domain.TemplateBuilding;
using Kroiko.Testing;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

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

    /// <summary>The manufacturer picker's input, whose value is the picked manufacturer's Bulgarian label.</summary>
    public static ILocator ManufacturerPicker(IPage page) => page.Locator(".mud-select").First.Locator("input");

    /// <summary>Fills the "Контакти на клиента" section.</summary>
    public static async Task FillContactsAsync(IPage page, string companyName, string mobileNumber)
    {
        await page.GetByLabel("Име на клиента").FillAsync(companyName);
        await page.GetByLabel("Телефон за връзка").FillAsync(mobileNumber);
    }

    /// <summary>The "Генерирай бланки за поръчка" button.</summary>
    public static ILocator GenerateButton(IPage page) =>
        page.GetByRole(AriaRole.Button, new() { Name = "Генерирай бланки за поръчка" });

    /// <summary>
    /// Answers the MegaTrading edge-banding dialog (ADR-0015) with <paramref name="pick"/> for every material: the width
    /// and the thickness where the material asks for them; then "Попълни и генерирай".
    /// </summary>
    public static async Task FillEdgeBandingAsync(IPage page, MegaTradingEdge pick)
    {
        var dialog = page.GetByRole(AriaRole.Dialog);
        var rows = dialog.Locator(".edge-banding-row");
        await Expect(rows.First).ToBeVisibleAsync();
        for (var i = 0; i < await rows.CountAsync(); i++)
        {
            foreach (var (part, value) in new[] { ("width", pick.Width), ("thickness", pick.Thickness) })
            {
                var select = rows.Nth(i).Locator($".edge-banding-{part}");
                if (await select.CountAsync() > 0)
                {
                    await select.ClickAsync();
                    await page.GetByRole(AriaRole.Option, new() { Name = value, Exact = true }).ClickAsync();
                }
            }
        }

        await dialog.GetByRole(AriaRole.Button, new() { Name = "Попълни и генерирай" }).ClickAsync();
        await Expect(dialog).ToHaveCountAsync(0);
    }

    /// <summary>The generated files' download links, one per file, in the order of the list.</summary>
    public static ILocator FileLinks(IPage page) => page.GetByTestId("generated-files").GetByRole(AriaRole.Link);

    /// <summary>A download as the order file it saved: the browser's suggested name and the downloaded bytes.</summary>
    public static async Task<FileSaveContext> ReadAsync(IDownload download)
    {
        await using var content = await download.CreateReadStreamAsync();
        using var bytes = new MemoryStream();
        await content.CopyToAsync(bytes);
        return new FileSaveContext(download.SuggestedFilename, bytes.ToArray());
    }

    /// <summary>
    /// Clicks "Изтегли всички" and collects the <paramref name="count"/> downloads it triggers, in the order they
    /// started, as the order files they saved: the browser's suggested name and the downloaded bytes.
    /// </summary>
    public static async Task<IReadOnlyList<FileSaveContext>> DownloadAllAsync(IPage page, int count)
    {
        var downloads = new ConcurrentQueue<IDownload>();
        var all = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnDownload(object? sender, IDownload download)
        {
            downloads.Enqueue(download);
            if (downloads.Count == count)
                all.TrySetResult();
        }

        page.Download += OnDownload;
        try
        {
            await page.GetByRole(AriaRole.Button, new() { Name = "Изтегли всички" }).ClickAsync();
            await all.Task.WaitAsync(TimeSpan.FromSeconds(30));
        }
        catch (TimeoutException e)
        {
            throw new TimeoutException($"Only {downloads.Count} of {count} downloads started within 30 s.", e);
        }
        finally
        {
            page.Download -= OnDownload;
        }

        var files = new List<FileSaveContext>();
        foreach (var download in downloads)
        {
            files.Add(await ReadAsync(download));
        }

        return files;
    }

    /// <summary>
    /// Converts <paramref name="fixture"/> with the manufacturer already picked, as the golden files were recorded:
    /// upload → the golden contacts (and the different edge colour, if given) → "Генерирай бланки за поръчка" (→ the
    /// MegaTrading edge-banding dialog answered with <paramref name="edgeBanding"/>, if given) → "Изтегли всички".
    /// Returns every generated file as downloaded.
    /// </summary>
    public static async Task<IReadOnlyList<FileSaveContext>> ConvertAsGoldenAsync(
        IPage page, string fixture, string? differentEdgeColor = null, MegaTradingEdge? edgeBanding = null)
    {
        await UploadAsync(page, fixture);
        if (differentEdgeColor is not null)
        {
            var edgeColour = page.GetByLabel("Кантиране с друг цвят");
            await edgeColour.FillAsync(differentEdgeColor);
            await edgeColour.PressAsync("Tab");
        }

        await FillContactsAsync(page, TestData.GoldenContact.CompanyName!, TestData.GoldenContact.MobileNumber!);
        await GenerateButton(page).ClickAsync();
        if (edgeBanding is { } pick)
        {
            await FillEdgeBandingAsync(page, pick);
        }

        var generated = page.GetByRole(AriaRole.Listitem);
        await Expect(generated.First).ToBeVisibleAsync();

        // The list renders at once; names.txt catches a file too few or too many.
        return await DownloadAllAsync(page, await generated.CountAsync());
    }

    /// <summary>
    /// <see cref="OrderFilesAssert.MatchGolden(string, string, IReadOnlyList{FileSaveContext})"/> that always
    /// compares, even under <c>UPDATE_GOLDEN=1</c>: the golden files are the Server's output as the domain tests
    /// record it, never the browser's.
    /// </summary>
    public static void MatchGolden(string fixture, string manufacturer, IReadOnlyList<FileSaveContext> files) =>
        OrderFilesAssert.MatchGolden(fixture, manufacturer, files, TestData.GoldenRoot, update: false);

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
