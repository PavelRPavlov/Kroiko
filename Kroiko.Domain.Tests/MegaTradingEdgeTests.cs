using FluentAssertions;
using Kroiko.Domain.CellsExtracting;
using Kroiko.Domain.TemplateBuilding;
using Xunit;
using static Kroiko.Domain.Tests.FormatTestData;

namespace Kroiko.Domain.Tests;

/// <summary>
/// A MegaTrading banded side as its <c>.cut_mt</c> cell holds it, <c>{width}/{thickness}</c>, with exactly the values
/// MegaTrading's software lists in its edge dropdowns (ADR-0015); and filling in the values Polyboard does not give.
/// </summary>
public sealed class MegaTradingEdgeTests
{
    [Theory]
    [InlineData("22/0.5", "22", "0.5")]
    [InlineData("42/0.8/1.0", "42", "0.8/1.0")]
    [InlineData("/2.0", "", "2.0")]
    [InlineData("28/", "28", "")]
    [InlineData("/", "", "")]
    [InlineData("Same", "Same", "")]
    public void A_cell_is_the_width_before_the_first_slash_and_the_thickness_after_it(string cell, string width, string thickness)
    {
        var edge = MegaTradingEdge.Parse(cell);

        edge.Should().Be(new MegaTradingEdge(width, thickness));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void An_empty_cell_is_no_edge(string? cell) => MegaTradingEdge.Parse(cell).Should().BeNull();

    [Fact]
    public void Every_width_and_thickness_MegaTrading_lists_round_trips_and_is_complete()
    {
        foreach (var width in MegaTradingEdge.Widths)
        {
            foreach (var thickness in MegaTradingEdge.Thicknesses)
            {
                var cell = new MegaTradingEdge(width, thickness).ToString();

                cell.Should().Be($"{width}/{thickness}");
                MegaTradingEdge.Parse(cell).Should().Be(new MegaTradingEdge(width, thickness))
                    .And.Match<MegaTradingEdge?>(e => e!.Value.IsComplete);
            }
        }
    }

    [Theory]
    [InlineData("/0.5")]
    [InlineData("22/")]
    [InlineData("/")]
    [InlineData("Same/0.5")]
    [InlineData("22/1.0")]
    [InlineData("18/0.5")]
    public void Anything_else_is_incomplete(string cell) => MegaTradingEdge.Parse(cell)!.Value.IsComplete.Should().BeFalse();

    [Theory]
    [InlineData(0, "")]
    [InlineData(0.4, "0.5")]
    [InlineData(0.5, "0.5")]
    [InlineData(0.8, "0.8/1.0")]
    [InlineData(1, "0.8/1.0")]
    [InlineData(1.5, "2.0")]
    [InlineData(2, "2.0")]
    public void A_Polyboard_thickness_maps_to_the_nearest_listed_one_and_none_stays_empty(double polyboard, string thickness) =>
        MegaTradingEdge.ThicknessFromPolyboard(polyboard).Should().Be(thickness);

    [Fact]
    public void A_Polyboard_edge_gets_its_thickness_and_leaves_the_width_to_pick()
    {
        var files = OrderFormats.For(SupportedCompanies.MegaTrading).CreateFiles(
            [Part("M1") with { HasTopEdge = true, TopEdgeThickness = 2, TopEdgeMaterial = "Same", HasLeftEdge = true }]);

        var detail = (MegaTradingDetail)files[0].Details[0];
        // The sides keep today's mapping: Polyboard's top is MegaTrading's right, its left MegaTrading's top.
        detail.RightEdge.Should().Be("/2.0");
        detail.TopEdge.Should().Be("/");
        detail.LeftEdge.Should().BeEmpty();
        detail.BottomEdge.Should().BeEmpty();
    }

    [Fact]
    public void Filling_completes_only_what_is_missing_and_only_for_the_picked_materials()
    {
        var files = new[]
        {
            new KroikoFile
            {
                FileName = "MegaTrading",
                Details =
                [
                    Detail("M1", left: "/", bottom: "/2.0", right: "42/", top: ""),
                    Detail("M1", left: "28/0.8/1.0", bottom: "Same/0.5"),
                    Detail("M2", left: "/"),
                ],
            },
        };

        var changed = MegaTradingEdges.FillMissing(files, new Dictionary<string, MegaTradingEdge> { ["M1"] = new("22", "0.5") });

        changed.Should().BeTrue();
        Sides(files[0].Details[0]).Should().Equal("22/0.5", "22/2.0", "42/0.5", "");
        Sides(files[0].Details[1]).Should().Equal("28/0.8/1.0", "22/0.5", "", "");
        Sides(files[0].Details[2]).Should().Equal("/", "", "", "");
        MegaTradingEdges.FindMissing(files)!.Materials.Should().ContainSingle().Which.Material.Should().Be("M2");
    }

    [Fact]
    public void A_pick_without_a_thickness_fills_only_the_widths()
    {
        var files = new[] { new KroikoFile { FileName = "MegaTrading", Details = [Detail("M1", left: "/", bottom: "/0.5")] } };

        MegaTradingEdges.FillMissing(files, new Dictionary<string, MegaTradingEdge> { ["M1"] = new("28", "") });

        Sides(files[0].Details[0]).Should().Equal("28/", "28/0.5", "", "");
    }

    [Fact]
    public void Filling_complete_edges_changes_nothing()
    {
        var files = new[] { new KroikoFile { FileName = "MegaTrading", Details = [Detail("M1", left: "22/0.5")] } };

        MegaTradingEdges.FillMissing(files, new Dictionary<string, MegaTradingEdge> { ["M1"] = new("42", "2.0") })
            .Should().BeFalse();
        MegaTradingEdges.FindMissing(files).Should().BeNull();
    }

    private static MegaTradingDetail Detail(string material, string left = "", string bottom = "", string right = "", string top = "") =>
        new() { Material = material, LeftEdge = left, BottomEdge = bottom, RightEdge = right, TopEdge = top };

    private static string[] Sides(IKroikoDetail detail)
    {
        var d = (MegaTradingDetail)detail;
        return [d.LeftEdge, d.BottomEdge, d.RightEdge, d.TopEdge];
    }
}
