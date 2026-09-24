using static Kroiko.Domain.TemplateBuilding.TableColumn;

namespace Kroiko.Domain.TemplateBuilding.Lonira;

internal sealed class LoniraTableRowProvider : ITableRowProvider
{
    // Lonira writes "" for an empty value (MegaTrading and Suliver pass null through).
    private static readonly TableColumn<LoniraDetail>[] Columns =
    [
        new(d => Invariant(d.Height), Centred),
        new(d => Invariant(d.Width), Centred),
        new(d => Invariant(d.Quantity), Centred),
        new(d => d.LoniraEdges ?? "", Left),
        new(d => d.Note ?? "", Left),
    ];

    public IEnumerable<Cell> GetTableRow(IKroikoDetail detail, int rowNumber, int startColumnNumber) =>
        Row(Columns, (LoniraDetail)detail, rowNumber, startColumnNumber);
}