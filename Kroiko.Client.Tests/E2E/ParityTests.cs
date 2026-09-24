using FluentAssertions;
using Kroiko.Domain.ExcelFilesGeneration;
using Kroiko.Testing;
using Microsoft.Playwright;
using Xunit;
using static Kroiko.Client.Tests.E2E.ConverterPage;
using static Microsoft.Playwright.Assertions;

namespace Kroiko.Client.Tests.E2E;

/// <summary>
/// Parity of the shipped artifact (docs/implementation/04-conversion-flow.md, step 6; ADR-0007 §5.1): the trimmed
/// Release build, in the browser, turns a fixture into the Server's order files — the golden files the domain tests
/// record — for every manufacturer, under the browser locales <c>bg-BG</c> (decimal comma) and <c>en-US</c>.
/// Lonira and MegaTrading read the 11-field format, Suliver the 23-field one; MegaTrading's fixture has ≤ 6 materials.
/// </summary>
[Collection(E2ECollection.Name)]
[Trait("Category", "E2E")]
public sealed class ParityTests(PublishedApp app)
{
    private const string Lonira = "Лонира, гр.София";
    private const string Suliver = "Съливер, гр.Пловдив (бул.Васил Априлов)";
    private const string MegaTrading = "Мега Трейдинг, гр.София";

    public static TheoryData<string, string, string, string> EveryManufacturerUnderBothLocales()
    {
        var data = new TheoryData<string, string, string, string>();
        foreach (var locale in new[] { "bg-BG", "en-US" })
        {
            data.Add(locale, Lonira, "wardrobes-4-materials", "Lonira");
            data.Add(locale, Suliver, "cabinet-23-field", "Suliver");
            data.Add(locale, MegaTrading, "bathroom-4-materials", "MegaTrading");
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EveryManufacturerUnderBothLocales))]
    public async Task The_downloaded_order_files_match_the_golden_files(
        string locale, string manufacturer, string fixture, string golden)
    {
        var files = await ConvertAsync(locale, manufacturer, fixture);

        OrderFilesAssert.MatchGolden(fixture, golden, files);
    }

    [Fact]
    public async Task Suliver_with_a_different_edge_colour_matches_the_golden_files()
    {
        // cabinet-23-field has "Different" edges; the operator's colour fills the template's {DifferentEdgeColor} cell.
        var files = await ConvertAsync("bg-BG", Suliver, "cabinet-23-field", differentEdgeColour: "Бял гланц");

        OrderFilesAssert.MatchGolden("cabinet-23-field", "Suliver-different-edge-color", files);
    }

    // Upload → contacts (the golden helper's) → generate → "Изтегли всички", on a fresh device with the browser's locale.
    private async Task<IReadOnlyList<FileSaveContext>> ConvertAsync(
        string locale, string manufacturer, string fixture, string? differentEdgeColour = null)
    {
        await using var context = await app.NewContextAsync(locale);
        var page = await context.NewPageAsync();
        var console = ConsoleErrors(page);
        await page.GotoAsync("/");
        await PickAsync(page, manufacturer);

        await Expect(page.Locator(".mud-select").First.Locator("input")).ToHaveValueAsync(manufacturer);

        await UploadAsync(page, fixture);
        if (differentEdgeColour is not null)
        {
            var edgeColour = page.GetByLabel("Кантиране с друг цвят");
            await edgeColour.FillAsync(differentEdgeColour);
            await edgeColour.PressAsync("Tab");
        }

        await FillContactsAsync(page, "Тест ООД", "0888123456");
        await GenerateButton(page).ClickAsync();
        var generated = page.GetByRole(AriaRole.Listitem);
        await Expect(generated.First).ToBeVisibleAsync();

        var files = await DownloadAllAsync(page, await generated.CountAsync());
        console.Should().BeEmpty();
        return files;
    }
}
