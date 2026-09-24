using System.Globalization;
using Kroiko.Domain;
using Kroiko.Domain.CellsExtracting;
using Kroiko.Domain.ExcelFilesGeneration;
using Kroiko.Testing;
using Xunit;

namespace ATAFurniture.Server.Tests;

/// <summary>
/// Characterization tests (01 step 3, ADR-0004 §8): every order file today's Server produces for each
/// valid fixture × manufacturer matches its golden file in <c>Kroiko.Testing/TestData/golden/</c>.
/// Re-record with <c>UPDATE_GOLDEN=1</c> only for an intended output change, and explain the diff in the PR.
/// </summary>
public sealed class GoldenTests
{
    private static readonly string[] ValidFixtures =
    [
        "bathroom-4-materials",
        "beds-5-materials",
        "cabinet-23-field",
        "kitchen-8-materials",
        "wardrobes-4-materials",
    ];

    private static readonly string[] Manufacturers =
    [
        nameof(SupportedCompanies.Lonira),
        nameof(SupportedCompanies.Suliver),
        nameof(SupportedCompanies.MegaTrading),
    ];

    // Fixtures with more than 6 materials (kitchen-8-materials) are recorded for MegaTrading as they are
    // today, with the .cut_mt header truncated to 6 material rows, as characterization (ADR-0007 §7).
    public static TheoryData<string, string> EveryValidFixtureAndManufacturer()
    {
        var data = new TheoryData<string, string>();
        foreach (var fixture in ValidFixtures)
        {
            foreach (var manufacturer in Manufacturers)
            {
                data.Add(fixture, manufacturer);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EveryValidFixtureAndManufacturer))]
    public async Task Todays_order_files_match_the_golden_files(string fixture, string manufacturer)
    {
        var files = await RunPipelineAsync(fixture, manufacturer);
        OrderFilesAssert.MatchGolden(fixture, manufacturer, files);
    }

    // ADR-0004 §4, §8: a bg-BG host (decimal comma) must produce the same order files as the invariant
    // recording. Green since phase 02 step 4 made every number↔string conversion invariant.
    [Theory]
    [MemberData(nameof(EveryValidFixtureAndManufacturer))]
    public async Task Order_files_under_bg_BG_match_the_golden_files(string fixture, string manufacturer)
    {
        var files = await RunPipelineAsync(fixture, manufacturer, culture: CultureInfo.GetCultureInfo("bg-BG"));

        // Always compare, even under UPDATE_GOLDEN=1: the golden files are the invariant-culture recording.
        OrderFilesAssert.MatchGolden(fixture, manufacturer, files, TestData.GoldenRoot, update: false);
    }

    [Fact]
    public async Task Suliver_with_a_different_edge_color_matches_the_golden_files()
    {
        // cabinet-23-field has oversized details ("СДВ с краен размер …") and "Different" edges
        // ("Кантиране с друг цвят"); the operator's colour fills the template's {DifferentEdgeColor} cell.
        const string fixture = "cabinet-23-field";
        var files = await RunPipelineAsync(fixture, nameof(SupportedCompanies.Suliver), differentEdgeColor: "Бял гланц");

        OrderFilesAssert.MatchGolden(fixture, "Suliver-different-edge-color", files);
    }

    // The only place that knows how today's Server turns a fixture into order files.
    // Phase 02 changes this method's body — and nothing else in the tests.
    private static async Task<IReadOnlyList<FileSaveContext>> RunPipelineAsync(
        string fixture, string manufacturer, string? differentEdgeColor = null, CultureInfo? culture = null)
    {
        var content = await File.ReadAllBytesAsync(TestData.Polyboard(fixture));

        // The host's culture for this run: invariant unless a test says otherwise (the bg-BG test).
        using var _ = new CultureScope((culture ?? CultureInfo.InvariantCulture).Name);

        // FileUploadComponent (through DetailsExtractorService): a file with any bad line loads nothing.
        var parsed = PolyboardParser.Parse(content);
        if (parsed.Errors.Count > 0 || parsed.Details.Count == 0)
        {
            // The live app stops here with an error, so there is nothing to record.
            throw new InvalidOperationException($"'{fixture}' does not parse cleanly; it is not a valid fixture.");
        }

        // FileDisplayComponent, then OrderHandlingComponent.GenerateFiles, through the manufacturer's
        // IOrderFormat (the Server resolves the same instance by its keyed DI name).
        var format = OrderFormats.For(Manufacturer(manufacturer));
        var files = format.CreateFiles(parsed.Details);
        var contact = new ContactInfo(CompanyName: "Тест ООД", MobileNumber: "0888123456");

        // ConverterContext.DifferentEdgeColor starts as string.Empty when the operator leaves it alone.
        return format.Generate(contact, files, differentEdgeColor ?? string.Empty);
    }

    private static SupportedCompany Manufacturer(string name) =>
        new[] { SupportedCompanies.Lonira, SupportedCompanies.Suliver, SupportedCompanies.MegaTrading }.Single(c => c.Name == name);
}
