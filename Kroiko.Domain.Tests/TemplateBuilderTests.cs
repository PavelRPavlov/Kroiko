using FluentAssertions;
using Kroiko.Domain.TemplateBuilding;
using Kroiko.Domain.TemplateBuilding.Lonira;
using Kroiko.Domain.TemplateBuilding.MegaTrading;
using Kroiko.Domain.TemplateBuilding.Suliver;
using Xunit;

namespace Kroiko.Domain.Tests;

/// <summary>
/// The template builders fill the manufacturer's <c>template.json</c>, which is compiled into
/// <c>Kroiko.Domain</c> (ADR-0004 §2): no path is passed, nothing is read from disk, and every build
/// starts from a fresh copy of the template. The golden tests pin the full output.
/// </summary>
public sealed class TemplateBuilderTests
{
    private static readonly ContactInfo Contact = new(CompanyName: "Тест ООД", MobileNumber: "0888123456");

    private static bool IsTemplateFlag(Cell cell) => cell.Value is { } value && value.StartsWith('{') && value.EndsWith('}');

    private static LoniraDetail LoniraDetail(string material) =>
        new() { Material = material, Height = 350, Width = 150, Quantity = 2 };

    [Fact]
    public async Task Lonira_fills_one_fresh_template_sheet_per_material_file()
    {
        var builder = new LoniraTemplateBuilder(new LoniraTableRowProvider());
        var files = new[]
        {
            new KroikoFile { FileName = "MELA_BL", Details = [LoniraDetail("MELA_BL")] },
            new KroikoFile { FileName = "OAK_18", Details = [LoniraDetail("OAK_18"), LoniraDetail("OAK_18")] },
        };

        var sheets = await builder.BuildTemplateAsync(Contact, files);

        sheets.Should().HaveCount(2);
        sheets.Should().AllSatisfy(sheet =>
        {
            sheet.Cells.Should().Contain(cell => cell.Value == "Тест ООД");
            sheet.Cells.Should().Contain(cell => cell.Value == "0888123456");
            sheet.Cells.Should().NotContain(cell => IsTemplateFlag(cell));
        });
        sheets[0].Cells.Should().Contain(cell => cell.Value == "MELA_BL").And.NotContain(cell => cell.Value == "OAK_18");
        sheets[1].Cells.Should().Contain(cell => cell.Value == "OAK_18").And.NotContain(cell => cell.Value == "MELA_BL");
        sheets[1].Cells.Count(cell => cell.Value == "350").Should().Be(2);
    }

    [Fact]
    public async Task Suliver_fills_a_fresh_template_sheet_on_every_build()
    {
        var builder = new SuliverTemplateBuilder(new SuliverTableRowProvider());
        KroikoFile[] files =
        [
            new() { FileName = "Suliver", Details = [new SuliverDetail { Material = "MELA_BL", Height = 350, Width = 150, Quantity = 2 }] },
        ];

        var first = (await builder.BuildTemplateAsync(Contact, files)).Should().ContainSingle().Subject;
        var second = (await builder.BuildTemplateAsync(Contact, files)).Should().ContainSingle().Subject;

        first.Cells.Should().Contain(cell => cell.Value == "MELA_BL");
        first.Cells.Should().NotContain(cell => cell.Value == TemplateBuilderBase.TableStartCellFlag);
        // The operator's colour is filled later, by FileGeneratorService.
        first.Cells.Should().ContainSingle(cell => cell.Value == TemplateBuilderBase.DifferentEdgeColorCellFlag);
        second.Should().NotBeSameAs(first);
        second.Cells.Should().BeEquivalentTo(first.Cells);
    }

    [Fact]
    public async Task MegaTrading_fills_a_fresh_template_sheet_on_every_build()
    {
        var builder = new MegaTradingTemplateBuilder(new MegaTradingTableRowProvider());
        KroikoFile[] files =
        [
            new()
            {
                FileName = "MegaTrading",
                Details =
                [
                    new MegaTradingDetail
                    {
                        Material = "MELA_BL", Height = 350, Width = 150, Quantity = 2,
                        LeftEdge = "", RightEdge = "", TopEdge = "", BottomEdge = "",
                    },
                ],
            },
        ];

        var first = (await builder.BuildTemplateAsync(Contact, files)).Should().ContainSingle().Subject;
        var second = (await builder.BuildTemplateAsync(Contact, files)).Should().ContainSingle().Subject;

        first.Cells.Should().Contain(cell => cell.Value == "MELA_BL");
        first.Cells.Should().NotContain(cell => IsTemplateFlag(cell));
        second.Should().NotBeSameAs(first);
        second.Cells.Should().BeEquivalentTo(first.Cells);
    }
}
