using FluentAssertions;
using Kroiko.Domain.TemplateBuilding;
using Kroiko.Domain.TemplateBuilding.Lonira;
using Kroiko.Domain.TemplateBuilding.MegaTrading;
using Kroiko.Domain.TemplateBuilding.Suliver;
using Kroiko.Testing;
using Xunit;

namespace Kroiko.Domain.Tests;

/// <summary>
/// Each manufacturer's row provider writes an explicit list of columns (ADR-0004 §5): the value, in
/// order, and whether the cell is centred (1) or not (0). Numbers are written in the invariant culture
/// (ADR-0004 §4), so these run under <c>bg-BG</c>. Today's empty-value quirks are kept: Lonira writes
/// <c>""</c>, MegaTrading and Suliver pass <c>null</c> through.
/// </summary>
public sealed class TableRowProviderTests
{
    private static IEnumerable<(string Name, string? Value, byte Alignment)> RowOf(
        ITableRowProvider provider, IKroikoDetail detail, int rowNumber, int startColumnNumber)
    {
        using var _ = CultureScope.BgBg();
        return provider.GetTableRow(detail, rowNumber, startColumnNumber)
            .Select(cell => (cell.Name, cell.Value, cell.ContentAlignment))
            .ToList();
    }

    [Fact]
    public void Lonira_writes_height_width_quantity_centred_then_edges_and_note()
    {
        var detail = new LoniraDetail
        {
            Material = "MELA_BL", Height = 609.5, Width = 300.25, Quantity = 2, LoniraEdges = "2 k 1 d", Note = "СДВ",
        };

        RowOf(new LoniraTableRowProvider(), detail, rowNumber: 7, startColumnNumber: 2).Should().Equal(
            ("B7", "609.5", 1),
            ("C7", "300.25", 1),
            ("D7", "2", 1),
            ("E7", "2 k 1 d", 0),
            ("F7", "СДВ", 0));
    }

    [Fact]
    public void Lonira_writes_an_empty_string_for_an_empty_value()
    {
        var detail = new LoniraDetail { Material = "MELA_BL", Height = 350, Width = 150, Quantity = 1, LoniraEdges = null, Note = "" };

        RowOf(new LoniraTableRowProvider(), detail, rowNumber: 1, startColumnNumber: 1)
            .Select(c => c.Value).Should().Equal("350", "150", "1", "", "");
    }

    [Fact]
    public void MegaTrading_writes_material_and_note_left_and_the_rest_centred()
    {
        var detail = new MegaTradingDetail
        {
            Material = "MELA_BL", Thickness = 18.5, Height = 609.18, Width = 300.5, Quantity = 3, Rotated = true,
            EdgeBandingMaterial = "MELA_BL", Note = "бележка",
            LeftEdge = "L", RightEdge = "R", BottomEdge = "B", TopEdge = "T",
        };

        RowOf(new MegaTradingTableRowProvider(), detail, rowNumber: 4, startColumnNumber: 1).Should().Equal(
            ("A4", "MELA_BL", 0),
            ("B4", "18.5", 1),
            ("C4", "609.18", 1),
            ("D4", "300.5", 1),
            ("E4", "3", 1),
            ("F4", "True", 1),
            ("G4", "MELA_BL", 1),
            ("H4", "бележка", 0));
    }

    [Fact]
    public void MegaTrading_passes_empty_values_through()
    {
        var detail = new MegaTradingDetail
        {
            Material = "MELA_BL", Thickness = 18, Height = 350, Width = 150, Quantity = 1,
            EdgeBandingMaterial = null, Note = "",
            LeftEdge = "", RightEdge = "", BottomEdge = "", TopEdge = "",
        };

        RowOf(new MegaTradingTableRowProvider(), detail, rowNumber: 1, startColumnNumber: 1)
            .Select(c => c.Value).Should().Equal("MELA_BL", "18", "350", "150", "1", "False", null, "");
    }

    [Fact]
    public void Suliver_writes_material_cabinet_and_note_left_and_the_rest_centred()
    {
        var detail = new SuliverDetail
        {
            Material = "MELA_BL", MaterialThickness = 18.5, IsGrainDirectionReversed = 2, Height = 609.18, Width = 300.5,
            Quantity = 3, Cabinet = "Шкаф 1", LongEdge = "1", LongEdge2 = "Фалц 13x4", ShortEdge = "0", ShortEdge2 = "2",
            Note = "Кантиране с друг цвят",
        };

        RowOf(new SuliverTableRowProvider(), detail, rowNumber: 12, startColumnNumber: 1).Should().Equal(
            ("A12", "MELA_BL", 0),
            ("B12", "18.5", 1),
            ("C12", "2", 1),
            ("D12", "609.18", 1),
            ("E12", "300.5", 1),
            ("F12", "3", 1),
            ("G12", "Шкаф 1", 0),
            ("H12", "1", 1),
            ("I12", "Фалц 13x4", 1),
            ("J12", "0", 1),
            ("K12", "2", 1),
            ("L12", "Кантиране с друг цвят", 0));
    }

    [Fact]
    public void Suliver_passes_empty_values_through()
    {
        var detail = new SuliverDetail
        {
            Material = "MELA_BL", MaterialThickness = 18, IsGrainDirectionReversed = 1, Height = 350, Width = 150,
            Quantity = 1, Cabinet = null, LongEdge = null, LongEdge2 = null, ShortEdge = null, ShortEdge2 = null, Note = "",
        };

        RowOf(new SuliverTableRowProvider(), detail, rowNumber: 1, startColumnNumber: 1)
            .Select(c => c.Value).Should().Equal("MELA_BL", "18", "1", "350", "150", "1", null, null, null, null, null, "");
    }
}
