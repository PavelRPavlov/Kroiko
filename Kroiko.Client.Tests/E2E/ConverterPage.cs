using System.Collections.Concurrent;
using Kroiko.Domain.ExcelFilesGeneration;
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
        finally
        {
            page.Download -= OnDownload;
        }

        var files = new List<FileSaveContext>();
        foreach (var download in downloads)
        {
            await using var content = await download.CreateReadStreamAsync();
            using var bytes = new MemoryStream();
            await content.CopyToAsync(bytes);
            files.Add(new FileSaveContext(download.SuggestedFilename, bytes.ToArray()));
        }

        return files;
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
