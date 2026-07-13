using System.Collections.ObjectModel;
using ATAFurniture.Server.Models;
using ATAFurniture.Server.TemplateBuilding;
using ATAFurniture.Server.TemplateBuilding.Lonira;
using ATAFurniture.Server.TemplateBuilding.Suliver;
using FluentAssertions;
using Kroiko.Domain;
using Kroiko.Domain.CellsExtracting;
using Kroiko.Domain.ExcelFilesGeneration;
using Kroiko.Domain.ExcelFilesGeneration.XlsxWrapper;
using Kroiko.Domain.TemplateBuilding;
using Kroiko.Domain.TemplateBuilding.Lonira;
using Kroiko.Domain.TemplateBuilding.MegaTrading;
using Kroiko.Domain.TemplateBuilding.Suliver;
using Kroiko.Domain.TextFileGeneration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ATAFurniture.Server.Tests;

/// <summary>
/// No-secrets smoke coverage for the .NET 10 upgrade: it runs the real
/// parse -> group -> generate pipeline (the same one the UI drives) for all three
/// companies against the checked-in Polyboard fixtures, and asserts real .xlsx / .cut_mt
/// output. This is the correctness-critical surface most exposed to the framework
/// and LargeXlsx bumps; it needs no DB, auth, blob storage or email.
/// </summary>
public class GenerationSmokeTests
{
    // The special column separator the MegaTrading .cut_mt integration requires.
    private const string MtSeparator = "╪";

    private static async Task<List<Detail>> Parse(string testFile)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestFiles", testFile);
        using var stream = new MemoryStream(await File.ReadAllBytesAsync(path));
        var extractor = new DetailsExtractorService(NullLogger<DetailsExtractorService>.Instance);
        return await extractor.ExtractDetails(stream);
    }

    private static string TemplatePath(string company) =>
        Path.Combine(AppContext.BaseDirectory, "TemplateBuilding", company, "template.json");

    private static FileGeneratorService NewGenerator() =>
        new(new ExcelFileGenerator(), new MegaTradingFileGenerator());

    private static ContactInfo Contact() => new()
    {
        CompanyName = "Test Company",
        MobileNumber = "0888123456",
        Email = "test@example.com"
    };

    // A real .xlsx is a ZIP archive; its first two bytes are the "PK" local-file-header magic.
    private static bool IsXlsx(FileSaveContext f) =>
        f.Content.Length > 4 && f.Content[0] == (byte)'P' && f.Content[1] == (byte)'K';

    // ---------- Parsing ----------

    [Fact]
    public async Task Parses_the_old_11_field_format()
    {
        var details = await Parse("file.txt");

        details.Should().HaveCount(76);
        details.Select(d => d.Material).Distinct().Should().HaveCount(4);
    }

    [Fact]
    public async Task Parses_the_latest_23_field_format()
    {
        var details = await Parse("Cabinet1.txt");

        details.Should().HaveCount(5);
    }

    [Fact]
    public async Task A_single_bad_line_discards_the_whole_file()
    {
        // Documents the current (known-issue) behaviour: one line with a wrong field
        // count makes the parser return nothing rather than skipping just that line.
        var details = await Parse("invalid format.txt");

        details.Should().BeEmpty();
    }

    // ---------- Generation ----------

    [Fact]
    public async Task Lonira_produces_one_xlsx_per_material()
    {
        var details = await Parse("file.txt");
        var files = details.GroupBy(d => d.Material)
            .Where(g => !string.IsNullOrEmpty(g.Key))
            .Select(g => new KroikoFile { FileName = g.Key, Details = g.ToList().ToLoniraDetails() })
            .ToList();

        var builder = new LoniraTemplateBuilder(new LoniraTableRowProvider(), TemplatePath("Lonira"));
        var result = await NewGenerator().CreateFiles(Contact(), files, builder, new LoniraFileNameProvider());

        result.Should().HaveCount(4);
        result.Should().OnlyContain(f => f.FileName.EndsWith(".xlsx"));
        result.Should().OnlyContain(f => IsXlsx(f));
    }

    [Fact]
    public async Task Suliver_produces_a_single_xlsx()
    {
        var details = new ObservableCollection<Detail>(await Parse("file.txt"));
        var files = new List<KroikoFile>
        {
            new() { FileName = "Suliver", Details = details.ToSuliverDetails() }
        };

        var builder = new SuliverTemplateBuilder(new SuliverTableRowProvider(), TemplatePath("Suliver"));
        var result = await NewGenerator().CreateFiles(Contact(), files, builder, new SuliverFileNameProvider());

        result.Should().ContainSingle();
        result[0].FileName.Should().EndWith(".xlsx");
        IsXlsx(result[0]).Should().BeTrue();
    }

    [Fact]
    public async Task MegaTrading_produces_an_xlsx_plus_a_cut_mt_with_exactly_six_material_rows()
    {
        var details = new ObservableCollection<Detail>(await Parse("file.txt"));
        var files = new List<KroikoFile>
        {
            new() { FileName = "MegaTrading", Details = details.ToMegaTradingDetails() }
        };

        var builder = new MegaTradingTemplateBuilder(new MegaTradingTableRowProvider(), TemplatePath("MegaTrading"));
        // generateTextFiles: true -> also emit the .cut_mt text file.
        var result = await NewGenerator().CreateFiles(
            Contact(), files, builder, new MegaTradingFileNameProvider(), generateTextFiles: true);

        var xlsx = result.Should().ContainSingle(f => f.FileName.EndsWith(".xlsx")).Subject;
        IsXlsx(xlsx).Should().BeTrue();

        var cutMt = result.Should().ContainSingle(f => f.FileName.EndsWith(".cut_mt")).Subject;
        var text = System.Text.Encoding.UTF8.GetString(cutMt.Content);
        text.Should().Contain(MtSeparator);

        // Invariant: the .cut_mt always carries exactly six material rows (real ones
        // padded with empty rows). Every material row ends "...2800<sep>2070<sep>".
        var materialRowMarker = $"2800{MtSeparator}2070";
        text.Split('\n').Count(line => line.Contains(materialRowMarker)).Should().Be(6);
    }
}
