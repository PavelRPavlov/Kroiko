using System.Collections.ObjectModel;
using ATAFurniture.Server.Models;
using FluentAssertions;
using Kroiko.Domain.CellsExtracting;
using Kroiko.Domain.TemplateBuilding;
using Kroiko.Testing;
using Xunit;

namespace ATAFurniture.Server.Tests;

/// <summary>
/// The "СДВ с краен размер" note writes the finished size in the invariant culture (ADR-0004 §4):
/// a <c>bg-BG</c> host writes <c>600.25x299.75</c>, not <c>600,25x299,75</c>.
/// </summary>
public sealed class OversizeNoteTests
{
    // 600.75 x 300.25 oversized by 0.5 on both sides: finished size 600.25 x 299.75.
    private static readonly Detail Oversized = new(
        Height: 600.75, Width: 300.25, Quantity: 1, Material: "MELA_BL", IsGrainDirectionReversed: false,
        HasTopEdge: false, HasBottomEdge: false, HasRightEdge: false, HasLeftEdge: false,
        Cabinet: "Шкаф", CuttingNumber: 1, MaterialThickness: 18,
        TopEdgeThickness: 0, BottomEdgeThickness: 0, RightEdgeThickness: 0, LeftEdgeThickness: 0,
        Reference: "1", TopEdgeMaterial: "", BottomEdgeMaterial: "", RightEdgeMaterial: "", LeftEdgeMaterial: "",
        OversizingHeight: 0.5, OversizingWidth: 0.5);

    [Fact]
    public void Lonira_writes_the_finished_size_in_the_invariant_culture_under_bg_BG()
    {
        List<IKroikoDetail> details;
        using (CultureScope.BgBg())
        {
            details = new List<Detail> { Oversized }.ToLoniraDetails();
        }

        details.Should().ContainSingle().Which.Note.Should().Be("СДВ с краен размер 600.25x299.75; ");
    }

    [Fact]
    public void Suliver_writes_the_finished_size_in_the_invariant_culture_under_bg_BG()
    {
        List<IKroikoDetail> details;
        using (CultureScope.BgBg())
        {
            details = new ObservableCollection<Detail> { Oversized }.ToSuliverDetails();
        }

        details.Should().ContainSingle().Which.Note.Should().Be("СДВ с краен размер 600.25x299.75; ");
    }
}
