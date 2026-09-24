using System.Text;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;
using static Kroiko.Client.Tests.E2E.ConverterPage;
using static Microsoft.Playwright.Assertions;

namespace Kroiko.Client.Tests.E2E;

/// <summary>
/// The Lonira, Suliver and MegaTrading tabs under the upload panel (docs/implementation/04-conversion-flow.md,
/// step 4): each grid edits the domain details in place, with the Server's editable fields (ADR-0005 §3), and the
/// MegaTrading tab shows a <c>TooManyMaterials</c> problem next to the material rename (ADR-0006 §3).
/// </summary>
[Collection(E2ECollection.Name)]
[Trait("Category", "E2E")]
public sealed class ConversionTabsTests(PublishedApp app)
{
    [Fact]
    public async Task Lonira_has_a_tab_per_material_whose_note_and_name_can_be_edited()
    {
        await using var context = await app.NewContextAsync();
        var page = await context.NewPageAsync();
        var console = ConsoleErrors(page);
        await page.GotoAsync("/");
        var tabs = page.GetByRole(AriaRole.Tab);

        // The device remembers no manufacturer, so the picker starts on Lonira and the upload makes its files.
        await UploadAsync(page, "wardrobes-4-materials");
        await Expect(page.GetByText("Прегледай информацията и редактирай при необходимост:")).ToBeVisibleAsync();
        await Expect(tabs).ToHaveTextAsync(["Basic white W908 ST2", "HDF 3 mm", "AGT White", "H3170 Dyb kendyl natur"]);
        await Expect(page.Locator(".mud-table th")).ToContainTextAsync(
            ["Размер по фладер [мм]", "Размер срещу фладер [мм]", "Брой детайли", "Кантиране", "Забележка"]);

        // Only the note is editable; a committed note is on the detail, so it is there after a tab switch.
        var firstRow = page.Locator(".mud-table-body tr").First;
        await Expect(firstRow.Locator("input")).ToHaveCountAsync(1);
        await firstRow.Locator("input").FillAsync("ръчна бележка");
        await firstRow.Locator("input").PressAsync("Tab");
        await tabs.Nth(1).ClickAsync();
        await Expect(page.GetByLabel("Материал", new() { Exact = true })).ToHaveValueAsync("HDF 3 mm");
        await tabs.Nth(0).ClickAsync();
        await Expect(page.Locator(".mud-table-body tr").First.Locator("input")).ToHaveValueAsync("ръчна бележка");

        // The file (material) name names the tab.
        var fileName = page.GetByLabel("Материал", new() { Exact = true });
        await fileName.FillAsync("Бяло ПДЧ");
        await fileName.PressAsync("Tab");
        await Expect(tabs.First).ToHaveTextAsync("Бяло ПДЧ");
        console.Should().BeEmpty();
    }

    [Fact]
    public async Task Suliver_asks_for_the_different_edge_colour_only_when_a_part_has_one()
    {
        await using var context = await app.NewContextAsync();
        var page = await context.NewPageAsync();
        var console = ConsoleErrors(page);
        await page.GotoAsync("/");

        await PickAsync(page, "Съливер, гр.Пловдив (бул.Васил Априлов)");
        await UploadAsync(page, "cabinet-23-field");
        await Expect(page.GetByRole(AriaRole.Tab)).ToHaveTextAsync(["Suliver"]);
        await Expect(page.Locator(".mud-table th")).ToContainTextAsync(
            ["Материал", "Деб. (мм)", "Рот*", "Дължина (мм)", "Ширина (мм)", "Брой", "Име Дет.",
             "Дъл.", "Дъл.2", "Шир.", "Шир.2", "Забележка"]);
        await Expect(page.Locator(".mud-table-body tr").First.Locator("input")).ToHaveCountAsync(1);

        var edgeColour = page.GetByLabel("Кантиране с друг цвят");
        await edgeColour.FillAsync("бял кант");
        await edgeColour.PressAsync("Tab");
        await Expect(edgeColour).ToHaveValueAsync("бял кант");

        // No part of this file has a different edge colour.
        await UploadAsync(page, "wardrobes-4-materials");
        await page.GetByRole(AriaRole.Dialog).GetByRole(AriaRole.Button, new() { Name = "Да" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Dialog)).ToHaveCountAsync(0);
        await Expect(edgeColour).ToHaveCountAsync(0);
        console.Should().BeEmpty();
    }

    [Fact]
    public async Task MegaTrading_names_too_many_materials_and_the_rename_can_merge_them()
    {
        await using var context = await app.NewContextAsync();
        var page = await context.NewPageAsync();
        var console = ConsoleErrors(page);
        await page.GotoAsync("/");

        await PickAsync(page, "Мега Трейдинг, гр.София");
        await UploadAsync(page, "kitchen-8-materials");
        await Expect(page.GetByRole(AriaRole.Tab)).ToHaveTextAsync(["MegaTrading"]);
        await Expect(page.Locator(".mud-table th")).ToContainTextAsync(
            ["Материал на детайла", "X - фладер", "Y", "Брой", "Rot.", "Ляво", "Долу", "Дясно", "Горе",
             "Материал - Кант", "Забележка"]);
        // The four edges, the edge-banding material and the note.
        await Expect(page.Locator(".mud-table-body tr").First.Locator("input")).ToHaveCountAsync(6);

        var problem = page.Locator(".mud-alert").Filter(new() { HasText = "най-много 6 материала" });
        await Expect(problem).ToContainTextAsync("използва 8");
        await Expect(problem).ToContainTextAsync("Cool Grey K0191 SU");
        await Expect(problem).ToContainTextAsync("Mirror");
        await Expect(problem).ToContainTextAsync("Използвани материали");

        var renames = page.Locator(".material-rename");
        await Expect(renames).ToHaveCountAsync(8);
        await renames.Filter(new() { HasText = "Mirror" }).Locator("input").FillAsync("Lemon sorbet");
        await renames.Filter(new() { HasText = "Med" }).Locator("input").FillAsync("Lemon sorbet");
        await page.GetByRole(AriaRole.Button, new() { Name = "Замести използваните с новите материали" }).ClickAsync();

        await Expect(problem).ToHaveCountAsync(0);
        await Expect(renames).ToHaveCountAsync(6);
        await Expect(renames.Filter(new() { HasText = "Mirror" })).ToHaveCountAsync(0);
        console.Should().BeEmpty();
    }

    [Fact]
    public async Task Grid_numbers_are_written_in_the_invariant_culture_under_bg_BG()
    {
        await using var context = await app.NewContextAsync("bg-BG");
        var page = await context.NewPageAsync();
        var console = ConsoleErrors(page);
        await page.GotoAsync("/");

        await PickAsync(page, "Лонира, гр.София");
        await UploadAsync(page, new FilePayload
        {
            Name = "fractional.txt",
            MimeType = "text/plain",
            Buffer = Encoding.UTF8.GetBytes("596.50;300.25;2;Test;0;0;0;0;0;[1K];1\r\n"),
        });

        var cells = page.Locator(".mud-table-body tr").First.Locator("td");
        await Expect(cells.Nth(0)).ToHaveTextAsync("596.5");
        await Expect(cells.Nth(1)).ToHaveTextAsync("300.25");
        console.Should().BeEmpty();
    }
}
