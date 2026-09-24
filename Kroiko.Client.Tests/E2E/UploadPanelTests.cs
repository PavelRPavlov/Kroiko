using System.Collections.Concurrent;
using FluentAssertions;
using Kroiko.Testing;
using Microsoft.Playwright;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace Kroiko.Client.Tests.E2E;

/// <summary>
/// The upload panel and the manufacturer picker on the Converter page, wired to <c>ConverterState</c>
/// (docs/implementation/04-conversion-flow.md, step 3): the bad-line alert, and the Bulgarian confirmation
/// before files are discarded (ADR-0005 §5, ADR-0006 §2).
/// </summary>
[Collection(E2ECollection.Name)]
[Trait("Category", "E2E")]
public sealed class UploadPanelTests(PublishedApp app)
{
    private const string DiscardQuestion = "Файловете за поръчка и всички редакции по тях ще бъдат изгубени. Да продължа ли?";

    [Fact]
    public async Task A_file_with_bad_lines_shows_the_first_ten_and_links_to_the_configuration()
    {
        await using var context = await app.NewContextAsync();
        var page = await context.NewPageAsync();
        var console = ConsoleErrors(page);
        await page.GotoAsync("/");

        await UploadAsync(page, "bad-lines-12");

        var alert = page.Locator(".mud-alert").Filter(new() { HasText = "не отговаря на изискванията за формат" });
        await Expect(alert).ToContainTextAsync("ред 2: 10 полета, очакват се 11 или 23");
        await Expect(alert).ToContainTextAsync("ред 12: полето „брой“ не е число");
        await Expect(alert.GetByRole(AriaRole.Listitem)).ToHaveCountAsync(11);
        await Expect(alert.GetByRole(AriaRole.Listitem).Last).ToHaveTextAsync("…и още 2");
        await Expect(page.GetByRole(AriaRole.Link, new() { Name = "ТУК" })).ToHaveAttributeAsync("href", "configuration");
        console.Should().BeEmpty();
    }

    [Fact]
    public async Task Switching_manufacturer_or_uploading_again_asks_first_and_no_keeps_the_Order()
    {
        await using var context = await app.NewContextAsync();
        var page = await context.NewPageAsync();
        var console = ConsoleErrors(page);
        await page.GotoAsync("/");
        var picker = page.Locator(".mud-select").First;

        // No files yet: neither picking a manufacturer nor uploading asks.
        await PickAsync(page, "Лонира, гр.София");
        await UploadAsync(page, "kitchen-8-materials");
        await Expect(page.GetByRole(AriaRole.Dialog)).ToHaveCountAsync(0);
        await Expect(page.Locator(".mud-alert")).ToHaveCountAsync(0);

        // Files exist: switching asks, and "Не" keeps Lonira in the picker.
        await PickAsync(page, "Мега Трейдинг, гр.София");
        var dialog = page.GetByRole(AriaRole.Dialog);
        await Expect(dialog).ToContainTextAsync(DiscardQuestion);
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Не" }).ClickAsync();
        await Expect(dialog).ToHaveCountAsync(0);
        await Expect(picker.Locator("input")).ToHaveValueAsync("Лонира, гр.София");

        // "Да" switches.
        await PickAsync(page, "Мега Трейдинг, гр.София");
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Да" }).ClickAsync();
        await Expect(dialog).ToHaveCountAsync(0);
        await Expect(picker.Locator("input")).ToHaveValueAsync("Мега Трейдинг, гр.София");

        // Uploading again asks too.
        await UploadAsync(page, "kitchen-8-materials");
        await Expect(dialog).ToContainTextAsync(DiscardQuestion);
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Не" }).ClickAsync();
        await Expect(dialog).ToHaveCountAsync(0);
        console.Should().BeEmpty();
    }

    [Fact]
    public async Task The_picker_starts_on_the_manufacturer_remembered_on_the_device()
    {
        await using var context = await app.NewContextAsync();
        await context.AddInitScriptAsync("""
            localStorage.setItem('kroiko.deviceSettings',
                JSON.stringify({ schemaVersion: 1, companyName: 'Мебели ООД', mobileNumber: '0888 123 456', manufacturer: 'Suliver' }));
            """);
        var page = await context.NewPageAsync();
        var console = ConsoleErrors(page);

        await page.GotoAsync("/");

        await Expect(page.Locator(".mud-select").First.Locator("input"))
            .ToHaveValueAsync("Съливер, гр.Пловдив (бул.Васил Априлов)");
        console.Should().BeEmpty();
    }

    private static Task UploadAsync(IPage page, string fixture) =>
        page.Locator("input[type=file]").SetInputFilesAsync(TestData.Polyboard(fixture));

    private static async Task PickAsync(IPage page, string manufacturer)
    {
        await page.Locator(".mud-select").First.ClickAsync();
        await page.GetByRole(AriaRole.Option, new() { Name = manufacturer }).ClickAsync();
    }

    // Errors the app logs to the console, e.g. an unhandled exception in a component.
    private static ConcurrentQueue<string> ConsoleErrors(IPage page)
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
