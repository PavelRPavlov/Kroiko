using static Kroiko.Domain.TemplateBuilding.TableColumn;

namespace Kroiko.Domain.TemplateBuilding.Suliver;

internal sealed class SuliverTableRowProvider : ITableRowProvider
{
    // Suliver passes an empty value through as it is (null stays null); Lonira writes "".
    private static readonly TableColumn<SuliverDetail>[] Columns =
    [
        new(d => d.Material, Left),
        new(d => Invariant(d.MaterialThickness), Centred),
        new(d => Invariant(d.IsGrainDirectionReversed), Centred),
        new(d => Invariant(d.Height), Centred),
        new(d => Invariant(d.Width), Centred),
        new(d => Invariant(d.Quantity), Centred),
        new(d => d.Cabinet, Left),
        new(d => d.LongEdge, Centred),
        new(d => d.LongEdge2, Centred),
        new(d => d.ShortEdge, Centred),
        new(d => d.ShortEdge2, Centred),
        new(d => d.Note, Left),
    ];

    public IEnumerable<Cell> GetTableRow(IKroikoDetail detail, int rowNumber, int startColumnNumber) =>
        Row(Columns, (SuliverDetail)detail, rowNumber, startColumnNumber);
}