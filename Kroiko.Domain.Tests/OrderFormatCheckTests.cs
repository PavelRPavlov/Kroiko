using FluentAssertions;
using Kroiko.Domain.CellsExtracting;
using Kroiko.Domain.TemplateBuilding;
using Xunit;
using static Kroiko.Domain.Tests.FormatTestData;

namespace Kroiko.Domain.Tests;

/// <summary>
/// <see cref="IOrderFormat.Check"/> finds the reasons a format refuses to generate (ADR-0006 §3): today only
/// MegaTrading's <c>.cut_mt</c> header, which has room for exactly 6 materials.
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
}
