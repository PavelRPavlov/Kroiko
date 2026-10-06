using System.Text;
using FluentAssertions;
using Kroiko.Domain.TemplateBuilding;
using Microsoft.Playwright;
using Xunit;
using static Kroiko.Client.Tests.E2E.ConverterPage;
using static Microsoft.Playwright.Assertions;

namespace Kroiko.Client.Tests.E2E;

/// <summary>
/// The Lonira, Suliver and MegaTrading tabs under the upload panel (docs/implementation/04-conversion-flow.md,
/// step 4): each grid edits the domain details in place, with the Server's editable fields (ADR-0005 §3), and the
/// MegaTrading tab shows a <c>TooManyMaterials</c> problem next to the material rename (ADR-0006 §3), and its edges
/// are a width and a thickness picked from MegaTrading's values, asked for before generating when missing (ADR-0015).
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
        // The four edges are pickers; the edge-banding material and the note are text.
        var firstRow = page.Locator(".mud-table-body tr").First;
        await Expect(firstRow.Locator(".megatrading-edge")).ToHaveCountAsync(4);
        await Expect(firstRow.Locator("input[type=text]:not([readonly])")).ToHaveCountAsync(2);

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
    public async Task MegaTrading_edges_are_picked_from_its_values_and_the_missing_ones_are_asked_for_before_generating()
    {
        await using var context = await app.NewContextAsync();
        var page = await context.NewPageAsync();
        var console = ConsoleErrors(page);
        await page.GotoAsync("/");

        await PickAsync(page, "Мега Трейдинг, гр.София");
        await UploadAsync(page, new FilePayload
        {
            Name = "two-edges.txt",
            MimeType = "text/plain",
            // One part of "Бяло" with its top and right edges banded; the 11-field format gives no width or thickness.
            Buffer = Encoding.UTF8.GetBytes("600;300;1;Бяло;0;1;0;1;0;[1K];1\r\n"),
        });

        // Polyboard's top is MegaTrading's right and its right MegaTrading's bottom: both miss both values, in red.
        var missing = page.GetByTestId("missing-edge-banding");
        await Expect(missing).ToContainTextAsync("На 2 канта");
        await Expect(missing).ToContainTextAsync("Бяло");
        var bottom = page.Locator(".megatrading-edge[data-side='Долу']");
        const string red = ".mud-input-control.mud-input-error";
        await Expect(bottom.Locator(red)).ToHaveCountAsync(2);
        await Expect(page.Locator($".megatrading-edge[data-side='Ляво'] {red}")).ToHaveCountAsync(0);

        // Picking both in the grid completes that side.
        await bottom.Locator(".edge-width").ClickAsync();
        await page.GetByRole(AriaRole.Option, new() { Name = "28", Exact = true }).ClickAsync();
        await bottom.Locator(".edge-thickness").ClickAsync();
        await page.GetByRole(AriaRole.Option, new() { Name = "2.0", Exact = true }).ClickAsync();
        await Expect(bottom.Locator(red)).ToHaveCountAsync(0);
        await Expect(missing).ToContainTextAsync("На 1 кант");

        // Generating asks for the rest; cancelling generates nothing.
        await FillContactsAsync(page, "Тест ООД", "0888123456");
        await GenerateButton(page).ClickAsync();
        var dialog = page.GetByRole(AriaRole.Dialog);
        await Expect(dialog.Locator(".edge-banding-row")).ToHaveCountAsync(1);
        await Expect(dialog.GetByRole(AriaRole.Button, new() { Name = "Попълни и генерирай" })).ToBeDisabledAsync();
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Отказ" }).ClickAsync();
        await Expect(dialog).ToHaveCountAsync(0);
        await Expect(page.GetByTestId("generated-files")).ToHaveCountAsync(0);

        // Answering it fills the right side only, keeps the bottom side's 28/2.0 and generates.
        await GenerateButton(page).ClickAsync();
        await FillEdgeBandingAsync(page, new MegaTradingEdge("22", "0.5"));
        await Expect(missing).ToHaveCountAsync(0);
        var files = await DownloadAllAsync(page, 2);
        var cutMt = Encoding.UTF8.GetString(files.Single(f => f.FileName.EndsWith(".cut_mt")).Content);
        cutMt.Should().Contain("Бяло╪600╪300╪1╪No╪╪28/2.0╪22/0.5╪╪");
        console.Should().BeEmpty();
    }

    [Fact]
    public async Task The_bucket_clears_a_MegaTrading_edge_so_it_is_neither_asked_for_nor_written()
    {
        await using var context = await app.NewContextAsync();
        var page = await context.NewPageAsync();
        var console = ConsoleErrors(page);
        await page.GotoAsync("/");

        await PickAsync(page, "Мега Трейдинг, гр.София");
        await UploadAsync(page, new FilePayload
        {
            Name = "two-edges.txt",
            MimeType = "text/plain",
            // Polyboard's top and right edges: MegaTrading's right and bottom, both missing their width and thickness.
            Buffer = Encoding.UTF8.GetBytes("600;300;1;Бяло;0;1;0;1;0;[1K];1\r\n"),
        });
        var missing = page.GetByTestId("missing-edge-banding");
        await Expect(missing).ToContainTextAsync("На 2 канта");

        // A side with no edge has nothing to clear.
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Ляво: изчисти канта" })).ToBeDisabledAsync();

        var right = page.Locator(".megatrading-edge[data-side='Дясно']");
        await page.GetByRole(AriaRole.Button, new() { Name = "Дясно: изчисти канта" }).ClickAsync();
        await Expect(right.Locator(".mud-input-control.mud-input-error")).ToHaveCountAsync(0);
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Дясно: изчисти канта" })).ToBeDisabledAsync();
        await Expect(missing).ToContainTextAsync("На 1 кант");

        // Only the bottom side is asked for and written; the cleared right side stays empty.
        await FillContactsAsync(page, "Тест ООД", "0888123456");
        await GenerateButton(page).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Dialog)).ToContainTextAsync("За 1 кант липсват");
        await FillEdgeBandingAsync(page, new MegaTradingEdge("22", "0.5"));
        var files = await DownloadAllAsync(page, 2);
        var cutMt = Encoding.UTF8.GetString(files.Single(f => f.FileName.EndsWith(".cut_mt")).Content);
        cutMt.Should().Contain("Бяло╪600╪300╪1╪No╪╪22/0.5╪╪╪");
        console.Should().BeEmpty();
    }

    [Theory]
    [InlineData(1280, 800)]
    [InlineData(768, 1024)]
    [InlineData(375, 812)]
    public async Task The_MegaTrading_grid_stays_put_when_the_last_warning_goes(int width, int height)
    {
        await using var context = await app.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(width, height);
        var console = ConsoleErrors(page);
        await page.GotoAsync("/");

        await PickAsync(page, "Мега Трейдинг, гр.София");
        await UploadAsync(page, new FilePayload
        {
            Name = "three-materials.txt",
            MimeType = "text/plain",
            // One banded side per material, so the warning lists three materials and wraps on narrow screens.
            Buffer = Encoding.UTF8.GetBytes(
                "2590;100;1;Бяло ПДЧ;0;1;0;0;0;[1K];1\r\n" +
                "600;300;1;Cool Grey K0191 SU;1;0;0;1;0;[2K];2\r\n" +
                "600;300;1;H3170 Dyb kendyl natur;1;0;1;0;0;[2K];3\r\n"),
        });
        var warning = page.GetByTestId("missing-edge-banding");
        await Expect(warning).ToContainTextAsync("На 3 канта");
        var grid = page.Locator(".mud-table");
        var before = await TopAsync(grid);

        var buckets = page.Locator(".edge-clear:not([disabled])");
        while (await buckets.CountAsync() > 0)
        {
            await buckets.First.ClickAsync();
        }

        await Expect(warning).ToHaveCountAsync(0);
        (await TopAsync(grid)).Should().Be(before, "the warnings keep their space when the last one goes");
        console.Should().BeEmpty();
    }

    // The element's top on the page, not the viewport, so scrolling does not count.
    private static Task<double> TopAsync(ILocator element) =>
        element.EvaluateAsync<double>("e => Math.round(e.getBoundingClientRect().top + window.scrollY)");

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
