using System.Text;
using FluentAssertions;
using Kroiko.Domain.CellsExtracting;
using Kroiko.Domain.TemplateBuilding;
using Kroiko.Testing;
using Xunit;

namespace Kroiko.Domain.Tests;

/// <summary>
/// <see cref="OrderFormats"/> holds one <see cref="IOrderFormat"/> per manufacturer (ADR-0004 §1), and
/// <see cref="IOrderFormat.CreateFiles"/> maps and groups the parsed Details into editable KroikoFiles.
/// The golden tests pin what <see cref="IOrderFormat.Generate"/> writes.
/// </summary>
public sealed class OrderFormatsTests
{
    private static Detail Part(string material, double height = 600, double width = 300) => new(
        Height: height, Width: width, Quantity: 1, Material: material, IsGrainDirectionReversed: false,
        HasTopEdge: false, HasBottomEdge: false, HasRightEdge: false, HasLeftEdge: false,
        Cabinet: "Шкаф", CuttingNumber: 1, MaterialThickness: 18,
        TopEdgeThickness: 0, BottomEdgeThickness: 0, RightEdgeThickness: 0, LeftEdgeThickness: 0,
        Reference: "1", TopEdgeMaterial: "", BottomEdgeMaterial: "", RightEdgeMaterial: "", LeftEdgeMaterial: "",
        OversizingHeight: 0, OversizingWidth: 0);

    // Theories name the manufacturer, as InlineData cannot hold a SupportedCompany.
    private static IOrderFormat FormatNamed(string manufacturer) =>
        OrderFormats.For(new[] { SupportedCompanies.Lonira, SupportedCompanies.Suliver, SupportedCompanies.MegaTrading }
            .Single(c => c.Name == manufacturer));

    [Fact]
    public void There_is_one_format_per_manufacturer()
    {
        OrderFormats.All.Select(f => f.Company).Should().Equal(
            SupportedCompanies.Lonira, SupportedCompanies.Suliver, SupportedCompanies.MegaTrading);
    }

    [Fact]
    public void For_finds_each_manufacturers_format()
    {
        OrderFormats.For(SupportedCompanies.Lonira).Company.Should().Be(SupportedCompanies.Lonira);
        OrderFormats.For(SupportedCompanies.Suliver).Company.Should().Be(SupportedCompanies.Suliver);
        OrderFormats.For(SupportedCompanies.MegaTrading).Company.Should().Be(SupportedCompanies.MegaTrading);
    }

    [Fact]
    public void For_rejects_a_company_the_domain_does_not_know()
    {
        var act = () => OrderFormats.For(new SupportedCompany("Unknown", "Непозната"));

        act.Should().Throw<ArgumentException>().Which.Message.Should().Contain("Unknown");
    }

    [Fact]
    public void Lonira_makes_one_file_per_material_in_order_of_first_use_and_skips_parts_without_one()
    {
        var files = OrderFormats.For(SupportedCompanies.Lonira)
            .CreateFiles([Part("MELA_BL"), Part("OAK_18"), Part(""), Part("MELA_BL")]);

        files.Select(f => (f.FileName, f.Details.Count)).Should().Equal(("MELA_BL", 2), ("OAK_18", 1));
        files.SelectMany(f => f.Details).Should().AllBeOfType<LoniraDetail>();
    }

    [Theory]
    [InlineData(nameof(SupportedCompanies.Suliver), typeof(SuliverDetail))]
    [InlineData(nameof(SupportedCompanies.MegaTrading), typeof(MegaTradingDetail))]
    public void Suliver_and_MegaTrading_make_one_file_with_every_part(string manufacturer, Type detailType)
    {
        var files = FormatNamed(manufacturer).CreateFiles([Part("MELA_BL"), Part("OAK_18"), Part("")]);

        var file = files.Should().ContainSingle().Subject;
        file.FileName.Should().Be(manufacturer);
        file.Details.Should().HaveCount(3).And.AllBeOfType(detailType);
    }

    [Theory]
    [InlineData(nameof(SupportedCompanies.Lonira))]
    [InlineData(nameof(SupportedCompanies.Suliver))]
    [InlineData(nameof(SupportedCompanies.MegaTrading))]
    public void No_details_make_no_files(string manufacturer)
    {
        FormatNamed(manufacturer).CreateFiles([]).Should().BeEmpty();
    }

    // ADR-0009: every .cut_mt line ends in CRLF on every host (Windows, Linux, the browser), not in
    // Environment.NewLine. One part makes 9 lines: the first row, 6 material rows, the column sizes, the part.
    [Fact]
    public void MegaTrading_ends_every_cut_mt_line_with_CRLF_whatever_the_OS()
    {
        var format = OrderFormats.For(SupportedCompanies.MegaTrading);
        var files = format.CreateFiles([Part("MELA_BL")]);

        var cutMt = format.Generate(new ContactInfo("Тест ООД", "0888123456"), files, differentEdgeColor: null)
            .Should().ContainSingle(f => f.FileName.EndsWith(".cut_mt")).Subject;
        var text = Encoding.UTF8.GetString(cutMt.Content);

        text.Should().EndWith("\r\n");
        text.Split("\r\n").Should().HaveCount(10, "9 lines, each ending in CRLF");
        text.Replace("\r\n", "").Should().NotContain("\n").And.NotContain("\r");
    }

    // The "СДВ с краен размер" note writes the finished size in the invariant culture (ADR-0004 §4):
    // a bg-BG host writes 600.25x299.75, not 600,25x299,75.
    [Theory]
    [InlineData(nameof(SupportedCompanies.Lonira))]
    [InlineData(nameof(SupportedCompanies.Suliver))]
    public void The_oversize_note_writes_the_finished_size_in_the_invariant_culture_under_bg_BG(string manufacturer)
    {
        // 600.75 x 300.25 oversized by 0.5 on both sides: finished size 600.25 x 299.75.
        var oversized = Part("MELA_BL", height: 600.75, width: 300.25) with { OversizingHeight = 0.5, OversizingWidth = 0.5 };

        IReadOnlyList<KroikoFile> files;
        using (CultureScope.BgBg())
        {
            files = FormatNamed(manufacturer).CreateFiles([oversized]);
        }

        files.Should().ContainSingle().Which.Details.Should().ContainSingle()
            .Which.Note.Should().Be("СДВ с краен размер 600.25x299.75; ");
    }
}
