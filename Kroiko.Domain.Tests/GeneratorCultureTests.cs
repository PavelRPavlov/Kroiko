using System.IO.Compression;
using System.Text;
using FluentAssertions;
using Kroiko.Domain.ExcelFilesGeneration;
using Kroiko.Domain.ExcelFilesGeneration.XlsxWrapper;
using Kroiko.Domain.TemplateBuilding;
using Kroiko.Domain.TextFileGeneration;
using Kroiko.Testing;
using Xunit;

namespace Kroiko.Domain.Tests;

/// <summary>
/// The generators read and write numbers in the invariant culture (ADR-0004 §4), whatever the host's
/// culture: a <c>bg-BG</c> host (decimal comma) produces the same files as the invariant recording.
/// </summary>
public sealed class GeneratorCultureTests
{
    private sealed class FixedFileName : IFileNameProvider
    {
        public string GetFileNameForSheet(ISheet sheet) => "order.xlsx";
    }

    private static string WorksheetXml(byte[] xlsx)
    {
        using var zip = new ZipArchive(new MemoryStream(xlsx), ZipArchiveMode.Read);
        using var reader = new StreamReader(zip.GetEntry("xl/worksheets/sheet1.xml")!.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }

    [Fact]
    public void Excel_writes_a_centred_invariant_decimal_as_a_number_under_bg_BG()
    {
        var sheet = new SheetBase
        {
            ColumnWidths = [10, 10],
            Cells = [new Cell("A1", 1) { Value = "609.5" }, new Cell("B1", 1) { Value = "Фалц 13x4" }],
        };

        List<FileSaveContext> files;
        using (CultureScope.BgBg())
        {
            files = ExcelFileGenerator.GenerateExcelFiles([sheet], new FixedFileName());
        }

        var xml = WorksheetXml(files.Should().ContainSingle().Subject.Content);
        xml.Should().Contain("<c r=\"A1\" s=\"1\"><v>609.5</v></c>");
        xml.Should().Contain("<c r=\"B1\" s=\"1\" t=\"inlineStr\"><is><t>Фалц 13x4</t></is></c>");
    }

    [Fact]
    public void Cut_mt_writes_invariant_decimals_under_bg_BG()
    {
        const string s = "╪";
        var detail = new MegaTradingDetail
        {
            Material = "MELA_BL", Thickness = 18.5, Height = 609.18, Width = 300.5, Quantity = 3, Rotated = true,
            EdgeBandingMaterial = "MELA_BL", Note = "",
            LeftEdge = "MELA_BL/0.5", RightEdge = "", BottomEdge = "", TopEdge = "",
        };
        var files = new[] { new KroikoFile { FileName = "MegaTrading", Details = [detail] } };

        string text;
        using (CultureScope.BgBg())
        {
            var file = MegaTradingFileGenerator
                .CreateTextBasedFile(new ContactInfo("Тест ООД", "0888123456"), files)
                .Should().ContainSingle().Subject;
            text = Encoding.UTF8.GetString(file.Content);
        }

        text.Should().Contain($"\r\nMELA_BL{s}18.5{s}True{s}True{s}2800{s}2070{s}\r\n");
        text.Should().Contain($"\r\nMELA_BL{s}609.18{s}300.5{s}3{s}Yes{s}MELA_BL/0.5{s}{s}{s}{s}MELA_BL{s}{s}\r\n");
    }
}
