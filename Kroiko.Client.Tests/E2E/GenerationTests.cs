using System.Text;
using FluentAssertions;
using Kroiko.Testing;
using Microsoft.Playwright;
using Xunit;
using static Kroiko.Client.Tests.E2E.ConverterPage;
using static Microsoft.Playwright.Assertions;

namespace Kroiko.Client.Tests.E2E;

/// <summary>
/// The contact section, "Генерирай бланки за поръчка" and "Изтегли всички" on the Converter page
/// (docs/implementation/04-conversion-flow.md, step 5): generating needs both contacts and no <c>Check</c> problem
/// (ADR-0005 §6, ADR-0006 §3), any edit discards the generated files (ADR-0005 §7), and the downloads carry the
/// sanitised names (ADR-0003 §5, §7).
/// </summary>
[Collection(E2ECollection.Name)]
[Trait("Category", "E2E")]
public sealed class GenerationTests(PublishedApp app)
{
    [Fact]
    public async Task Generating_needs_both_contacts_and_downloading_all_saves_the_order_files()
    {
        await using var context = await app.NewContextAsync("bg-BG");
        var page = await context.NewPageAsync();
        var console = ConsoleErrors(page);
        await page.GotoAsync("/");
        var generate = GenerateButton(page);

        // A first visit: Lonira, and no contacts to pre-fill.
        await UploadAsync(page, "wardrobes-4-materials");
        await Expect(page.GetByText("Контакти на клиента")).ToBeVisibleAsync();
        await Expect(generate).ToBeDisabledAsync();
        await page.GetByLabel("Име на клиента").FillAsync("Тест ООД");
        await Expect(generate).ToBeDisabledAsync();
        await page.GetByLabel("Телефон за връзка").FillAsync("   ");
        await Expect(generate).ToBeDisabledAsync();
        await page.GetByLabel("Телефон за връзка").FillAsync("0888123456");
        await Expect(generate).ToBeEnabledAsync();

        await generate.ClickAsync();
        await Expect(page.GetByRole(AriaRole.Listitem)).ToHaveTextAsync(
            ["Basic white W908 ST2.xlsx", "HDF 3 mm.xlsx", "AGT White.xlsx", "H3170 Dyb kendyl natur.xlsx"]);

        // The golden helper's contacts, so the downloads are the Server's order files.
        var files = await DownloadAllAsync(page, count: 4);
        OrderFilesAssert.MatchGolden("wardrobes-4-materials", "Lonira", files);
        console.Should().BeEmpty();
    }

    [Fact]
    public async Task After_generating_a_reload_pre_fills_the_contacts_and_the_manufacturer()
    {
        await using var context = await app.NewContextAsync();
        var page = await context.NewPageAsync();
        var console = ConsoleErrors(page);
        await page.GotoAsync("/");
        var picker = page.Locator(".mud-select").First.Locator("input");
        const string suliver = "Съливер, гр.Пловдив (бул.Васил Априлов)";

        // Suliver, not the first-visit default, so a pre-filled picker is the remembered manufacturer.
        await PickAsync(page, suliver);
        await UploadAsync(page, "cabinet-23-field");
        await FillContactsAsync(page, "Мебели ООД", "0888 765 432");
        await GenerateButton(page).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Listitem)).ToHaveCountAsync(1);

        await page.ReloadAsync();

        await Expect(picker).ToHaveValueAsync(suliver);
        await UploadAsync(page, "cabinet-23-field");
        await Expect(page.GetByLabel("Име на клиента")).ToHaveValueAsync("Мебели ООД");
        await Expect(page.GetByLabel("Телефон за връзка")).ToHaveValueAsync("0888 765 432");
        await Expect(GenerateButton(page)).ToBeEnabledAsync();
        console.Should().BeEmpty();
    }

    [Fact]
    public async Task An_edit_after_generating_discards_the_generated_files()
    {
        await using var context = await app.NewContextAsync();
        var page = await context.NewPageAsync();
        var console = ConsoleErrors(page);
        await page.GotoAsync("/");
        var generated = page.GetByRole(AriaRole.Listitem);
        var downloadAll = page.GetByRole(AriaRole.Button, new() { Name = "Изтегли всички" });

        await UploadAsync(page, "wardrobes-4-materials");
        await FillContactsAsync(page, "Тест ООД", "0888123456");
        await GenerateButton(page).ClickAsync();
        await Expect(generated).ToHaveCountAsync(4);
        await Expect(downloadAll).ToBeVisibleAsync();

        var note = page.Locator(".mud-table-body tr").First.Locator("input");
        await note.FillAsync("ръчна бележка");
        await note.PressAsync("Tab");

        await Expect(generated).ToHaveCountAsync(0);
        await Expect(downloadAll).ToHaveCountAsync(0);
        console.Should().BeEmpty();
    }

    [Fact]
    public async Task A_MegaTrading_order_with_too_many_materials_cannot_be_generated()
    {
        await using var context = await app.NewContextAsync();
        var page = await context.NewPageAsync();
        var console = ConsoleErrors(page);
        await page.GotoAsync("/");

        await PickAsync(page, "Мега Трейдинг, гр.София");
        await UploadAsync(page, "kitchen-8-materials");
        await FillContactsAsync(page, "Тест ООД", "0888123456");

        await Expect(GenerateButton(page)).ToBeDisabledAsync();
        console.Should().BeEmpty();
    }

    [Fact]
    public async Task The_files_are_listed_and_downloaded_under_their_sanitised_names()
    {
        await using var context = await app.NewContextAsync();
        var page = await context.NewPageAsync();
        var console = ConsoleErrors(page);
        await page.GotoAsync("/");

        // Lonira names each file after its material, which is free text.
        await UploadAsync(page, new FilePayload
        {
            Name = "names.txt",
            MimeType = "text/plain",
            Buffer = Encoding.UTF8.GetBytes("214.0;247.0;1;CON;0;0;0;1;0;Model[0];1\r\n250.0;247.0;1;Egger W1000: \"бял\";0;0;0;1;0;Model[0];2\r\n"),
        });
        await FillContactsAsync(page, "Тест ООД", "0888123456");
        await GenerateButton(page).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Listitem)).ToHaveTextAsync(["_CON.xlsx", "Egger W1000_ _бял_.xlsx"]);

        var files = await DownloadAllAsync(page, count: 2);

        files.Select(f => f.FileName).Should().Equal("_CON.xlsx", "Egger W1000_ _бял_.xlsx");
        console.Should().BeEmpty();
    }
}
