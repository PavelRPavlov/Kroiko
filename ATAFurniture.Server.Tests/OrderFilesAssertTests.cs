using System.IO.Compression;
using System.Text;
using FluentAssertions;
using Kroiko.Domain.ExcelFilesGeneration;
using Kroiko.Testing;
using Xunit;

namespace ATAFurniture.Server.Tests;

/// <summary>
/// The golden-file comparison itself (01 step 2). Each test records into and compares against
/// its own temporary golden root, never the checked-in golden files.
/// </summary>
public sealed class OrderFilesAssertTests : IDisposable
{
    private const string Fixture = "kitchen-8-materials";
    private const string Manufacturer = "MegaTrading";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "kroiko-golden-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private void Record(params FileSaveContext[] files) =>
        OrderFilesAssert.MatchGolden(Fixture, Manufacturer, files, _root, update: true);

    private void Compare(params FileSaveContext[] files) =>
        OrderFilesAssert.MatchGolden(Fixture, Manufacturer, files, _root, update: false);

    private static FileSaveContext Xlsx(string name, string sheetXml, string coreXml = "<core/>")
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddEntry(zip, "[Content_Types].xml", "<Types/>");
            AddEntry(zip, "docProps/core.xml", coreXml);
            AddEntry(zip, "xl/worksheets/sheet1.xml", sheetXml);
        }

        return new FileSaveContext(name, stream.ToArray());
    }

    private static FileSaveContext XlsxWithSheets(string name, params string[] sheetXmls)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            for (var i = 0; i < sheetXmls.Length; i++)
            {
                AddEntry(zip, $"xl/worksheets/sheet{i + 1}.xml", sheetXmls[i]);
            }
        }

        return new FileSaveContext(name, stream.ToArray());
    }

    private static void AddEntry(ZipArchive zip, string name, string content)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private static FileSaveContext CutMt(string name, string text) =>
        new(name, new UTF8Encoding(false).GetBytes(text));

    [Fact]
    public void Recorded_files_match_themselves()
    {
        var xlsx = Xlsx("2026-09-24_Тест ООД.xlsx", "<worksheet><row>ПДЧ</row></worksheet>");
        var cutMt = CutMt("Тест ООД.cut_mt", "╪╪True\r\nПДЧ╪18╪True╪True╪2800╪2070╪\r\n");

        Record(xlsx, cutMt);

        var act = () => Compare(xlsx, cutMt);
        act.Should().NotThrow();
    }

    [Fact]
    public void Files_generated_on_another_day_still_match()
    {
        Record(
            Xlsx("2026-09-24_Тест ООД.xlsx", "<worksheet/>"),
            CutMt("Тест ООД.cut_mt", "2026-09-24╪╪True\r\n"));

        var act = () => Compare(
            Xlsx("2027-01-31_Тест ООД.xlsx", "<worksheet/>"),
            CutMt("Тест ООД.cut_mt", "2027-01-31╪╪True\r\n"));

        act.Should().NotThrow();
    }

    [Fact]
    public void Names_txt_records_the_file_names_in_generation_order_with_the_date_normalised()
    {
        Record(
            CutMt("Тест ООД.cut_mt", "╪╪True\r\n"),
            Xlsx("2026-09-24_Тест ООД.xlsx", "<worksheet/>"));

        File.ReadAllText(Path.Combine(_root, Fixture, Manufacturer, "names.txt"))
            .Should().Be("Тест ООД.cut_mt\n{date}_Тест ООД.xlsx\n");
    }

    [Fact]
    public void Only_worksheet_xml_counts_for_an_xlsx()
    {
        Record(Xlsx("Тест ООД.xlsx", "<worksheet/>", coreXml: "<created>2026-09-24T10:00:00Z</created>"));

        var act = () => Compare(Xlsx("Тест ООД.xlsx", "<worksheet/>", coreXml: "<created>2027-01-31T08:30:00Z</created>"));

        act.Should().NotThrow();
    }

    [Fact]
    public void An_xlsx_that_lost_a_worksheet_does_not_match()
    {
        Record(XlsxWithSheets("Тест ООД.xlsx", "<worksheet>1</worksheet>", "<worksheet>2</worksheet>"));

        var act = () => Compare(XlsxWithSheets("Тест ООД.xlsx", "<worksheet>1</worksheet>"));

        act.Should().Throw<GoldenFileMismatchException>()
            .Which.Message.Should().Contain("Тест ООД.xlsx").And.Contain("xl/worksheets/sheet2.xml");
    }

    [Fact]
    public void Re_recording_replaces_the_whole_golden_set()
    {
        Record(Xlsx("Стар материал.xlsx", "<worksheet/>"), CutMt("Тест ООД.cut_mt", "╪╪True\r\n"));

        Record(Xlsx("Нов материал.xlsx", "<worksheet/>"));

        Directory.EnumerateFiles(Path.Combine(_root, Fixture, Manufacturer), "*", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(Path.Combine(_root, Fixture, Manufacturer), p).Replace('\\', '/'))
            .Should().BeEquivalentTo("names.txt", "Нов материал.xlsx/xl/worksheets/sheet1.xml");
    }

    [Fact]
    public void Golden_files_are_recorded_into_the_source_tree_and_read_from_the_output_copy()
    {
        var source = TestData.GoldenSourceRoot;

        Path.GetFullPath(source).Replace('\\', '/').Should().EndWith("/Kroiko.Testing/TestData/golden");
        File.Exists(Path.Combine(source, "..", "..", "Kroiko.Testing.csproj")).Should().BeTrue();
        Path.GetFullPath(source).Should().NotStartWith(AppContext.BaseDirectory);

        TestData.GoldenRoot.Should().Be(Path.Combine(AppContext.BaseDirectory, "TestData", "golden"));
    }

    [Fact]
    public void Nothing_recorded_fails_with_a_mismatch_that_says_how_to_record()
    {
        var act = () => Compare(CutMt("Тест ООД.cut_mt", "╪╪True\r\n"));

        act.Should().Throw<GoldenFileMismatchException>()
            .Which.Message.Should().Contain("names.txt").And.Contain("UPDATE_GOLDEN=1");
    }

    [Fact]
    public void A_changed_worksheet_names_the_fixture_manufacturer_file_and_first_differing_line()
    {
        Record(Xlsx("Тест ООД.xlsx", "<worksheet>\n<row>1</row>\n<row>ПДЧ 18</row>\n</worksheet>"));

        var act = () => Compare(Xlsx("Тест ООД.xlsx", "<worksheet>\n<row>1</row>\n<row>ПДЧ 19</row>\n</worksheet>"));

        act.Should().Throw<GoldenFileMismatchException>()
            .Which.Message.Should()
            .Contain("kitchen-8-materials").And
            .Contain("MegaTrading").And
            .Contain("Тест ООД.xlsx").And
            .Contain("xl/worksheets/sheet1.xml").And
            .Contain("line 3").And
            .Contain("<row>ПДЧ 18</row>").And
            .Contain("<row>ПДЧ 19</row>");
    }
}
