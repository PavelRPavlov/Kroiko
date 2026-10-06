using FluentAssertions;
using Kroiko.Domain.CellsExtracting;
using Kroiko.Domain.TemplateBuilding;
using Kroiko.Domain.TemplateBuilding.Lonira;
using Kroiko.Domain.TemplateBuilding.MegaTrading;
using Xunit;
using static Kroiko.Domain.Tests.FormatTestData;

namespace Kroiko.Domain.Tests;

/// <summary>
/// The edge-banding summary — Lonira's "Кантиране" and MegaTrading's "Забележка" (ADR-0016): how many short (k) and
/// long (d) sides of a part are banded, counted along the grain.
/// </summary>
public sealed class EdgeBandingSummaryTests
{
    [Theory]
    // A part wider than high: its left and right sides are short, its top and bottom long.
    [InlineData(300, 600, false, true, true, true, true, "2 k 2 d")]
    [InlineData(300, 600, false, false, false, true, true, "2 k")]
    [InlineData(300, 600, false, true, false, false, false, "1 d")]
    [InlineData(300, 600, false, true, false, false, true, "1 k 1 d")]
    // A part higher than wide: its left and right sides are long, its top and bottom short.
    [InlineData(600, 300, false, false, false, true, true, "2 d")]
    [InlineData(600, 300, false, true, true, false, false, "2 k")]
    // A square part counts as wider than high.
    [InlineData(400, 400, false, false, false, true, false, "1 k")]
    // A part whose grain is reversed is turned first: higher than wide becomes wider than high.
    [InlineData(600, 300, true, false, false, true, true, "2 k")]
    [InlineData(600, 300, false, false, false, false, false, "")]
    public void Counts_the_banded_short_and_long_sides(
        double height, double width, bool grainReversed, bool top, bool bottom, bool right, bool left, string summary) =>
        EdgeBandingSummary.Describe(height, width, grainReversed, top, bottom, right, left).Should().Be(summary);

    public static TheoryData<double, double, bool, int> EveryPart()
    {
        var data = new TheoryData<double, double, bool, int>();
        foreach (var (height, width) in new[] { (600.0, 300.0), (300.0, 600.0), (400.0, 400.0) })
        {
            foreach (var grainReversed in new[] { false, true })
            {
                for (var sides = 0; sides < 16; sides++)
                {
                    data.Add(height, width, grainReversed, sides);
                }
            }
        }

        return data;
    }

    // "Exactly as Lonira": the same Polyboard part gets the same summary in both sheets, whatever its sides and grain.
    [Theory]
    [MemberData(nameof(EveryPart))]
    public void MegaTradings_note_is_Loniras_edge_column(double height, double width, bool grainReversed, int sides)
    {
        var part = Part("M1", height, width) with
        {
            IsGrainDirectionReversed = grainReversed,
            HasTopEdge = (sides & 1) != 0,
            HasBottomEdge = (sides & 2) != 0,
            HasRightEdge = (sides & 4) != 0,
            HasLeftEdge = (sides & 8) != 0,
        };

        var lonira = (LoniraDetail)OrderFormats.For(SupportedCompanies.Lonira).CreateFiles([part])[0].Details[0];
        var megaTrading = (MegaTradingDetail)OrderFormats.For(SupportedCompanies.MegaTrading).CreateFiles([part])[0].Details[0];

        MegaTradingOrderFormat.EdgeSummary(megaTrading).Should().Be(lonira.LoniraEdges);
    }

    [Fact]
    public void MegaTradings_note_counts_the_edges_as_the_operator_left_them()
    {
        var part = Part("M1", height: 300, width: 600) with { HasTopEdge = true, HasBottomEdge = true, HasLeftEdge = true };
        var detail = (MegaTradingDetail)OrderFormats.For(SupportedCompanies.MegaTrading).CreateFiles([part])[0].Details[0];
        MegaTradingOrderFormat.EdgeSummary(detail).Should().Be("1 k 2 d");

        // The bucket clears Polyboard's left side (MegaTrading's top); a picked width and thickness change nothing.
        detail.TopEdge = string.Empty;
        detail.RightEdge = "22/0.5";

        MegaTradingOrderFormat.EdgeSummary(detail).Should().Be("2 d");
    }
}
