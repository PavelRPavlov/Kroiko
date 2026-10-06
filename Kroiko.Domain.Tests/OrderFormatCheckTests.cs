using FluentAssertions;
using Kroiko.Domain.CellsExtracting;
using Kroiko.Domain.TemplateBuilding;
using Xunit;
using static Kroiko.Domain.Tests.FormatTestData;

namespace Kroiko.Domain.Tests;

/// <summary>
/// <see cref="IOrderFormat.Check"/> finds the reasons a format refuses to generate (ADR-0006 §3): today only
/// MegaTrading's — its <c>.cut_mt</c> header, which has room for exactly 6 materials, and banded sides with no
/// edge-banding width or thickness its software accepts (ADR-0015).
/// </summary>
public sealed class OrderFormatCheckTests
{
    private static readonly string[] SevenMaterials = ["M1", "M2", "M3", "M4", "M5", "M6", "M7"];

    [Fact]
    public void MegaTrading_refuses_seven_materials()
    {
        var format = OrderFormats.For(SupportedCompanies.MegaTrading);
        var files = format.CreateFiles(PartsOf([.. SevenMaterials, "M1"]));

        var problem = format.Check(files).Should().ContainSingle().Which.Should().BeOfType<TooManyMaterials>().Subject;
        problem.Max.Should().Be(6);
        problem.Materials.Should().Equal(SevenMaterials);
    }

    [Fact]
    public void MegaTrading_accepts_six_materials()
    {
        var format = OrderFormats.For(SupportedCompanies.MegaTrading);
        var files = format.CreateFiles(PartsOf(SevenMaterials[..6]));

        format.Check(files).Should().BeEmpty();
    }

    // The operator's material rename (the MegaTrading tab) edits each detail's Material in place.
    [Fact]
    public void MegaTrading_accepts_seven_materials_once_a_rename_merges_two_of_them()
    {
        var format = OrderFormats.For(SupportedCompanies.MegaTrading);
        var files = format.CreateFiles(PartsOf(SevenMaterials));

        foreach (var detail in files.SelectMany(f => f.Details).Where(d => d.Material == "M7"))
        {
            detail.Material = "M1";
        }

        format.Check(files).Should().BeEmpty();
    }

    [Fact]
    public void MegaTrading_refuses_six_materials_once_a_rename_splits_one_into_a_seventh()
    {
        var format = OrderFormats.For(SupportedCompanies.MegaTrading);
        var files = format.CreateFiles(PartsOf([.. SevenMaterials[..6], "M1"]));
        format.Check(files).Should().BeEmpty();

        files.SelectMany(f => f.Details).Last().Material = "M7";

        format.Check(files).Should().ContainSingle()
            .Which.Should().BeOfType<TooManyMaterials>().Which.Materials.Should().Equal(SevenMaterials);
    }

    [Theory]
    [InlineData(nameof(SupportedCompanies.Lonira))]
    [InlineData(nameof(SupportedCompanies.Suliver))]
    public void Lonira_and_Suliver_have_no_material_limit(string manufacturer)
    {
        var format = FormatNamed(manufacturer);
        var files = format.CreateFiles(PartsOf(SevenMaterials));

        format.Check(files).Should().BeEmpty();
    }

    [Fact]
    public void MegaTrading_names_the_materials_whose_banded_sides_miss_a_width_or_a_thickness()
    {
        var format = OrderFormats.For(SupportedCompanies.MegaTrading);
        var files = format.CreateFiles(
        [
            Part("M1") with { HasTopEdge = true, TopEdgeThickness = 2 },
            Part("M2"),
            Part("M3") with { HasLeftEdge = true, HasRightEdge = true },
            Part("M1") with { HasBottomEdge = true, BottomEdgeThickness = 0.5 },
        ]);

        format.Check(files).Should().ContainSingle().Which.Should().BeOfType<MissingEdgeBanding>()
            .Which.Materials.Should().Equal(
                new MaterialEdgeBanding("M1", Edges: 2, NeedsWidth: true, NeedsThickness: false),
                new MaterialEdgeBanding("M3", Edges: 2, NeedsWidth: true, NeedsThickness: true));
    }

    [Fact]
    public void MegaTrading_accepts_banded_sides_once_their_width_and_thickness_are_picked()
    {
        var format = OrderFormats.For(SupportedCompanies.MegaTrading);
        var files = format.CreateFiles([Part("M1") with { HasTopEdge = true }]);

        ((MegaTradingDetail)files[0].Details[0]).RightEdge = "28/0.8/1.0";

        format.Check(files).Should().BeEmpty();
    }

    [Fact]
    public void MegaTrading_reports_too_many_materials_before_the_missing_edge_banding()
    {
        var format = OrderFormats.For(SupportedCompanies.MegaTrading);
        var files = format.CreateFiles([.. PartsOf(SevenMaterials), Part("M1") with { HasTopEdge = true }]);

        format.Check(files).Select(p => p.GetType()).Should().Equal(typeof(TooManyMaterials), typeof(MissingEdgeBanding));
    }
}
