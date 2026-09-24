using static Kroiko.Domain.TemplateBuilding.TableColumn;

namespace Kroiko.Domain.TemplateBuilding.MegaTrading;

public class MegaTradingTableRowProvider: ITableRowProvider
{
    // MegaTrading passes an empty value through as it is (null stays null); Lonira writes "".
    private static readonly TableColumn<MegaTradingDetail>[] Columns =
    [
        new(d => d.Material, Left),
        new(d => Invariant(d.Thickness), Centred),
        new(d => Invariant(d.Height), Centred),
        new(d => Invariant(d.Width), Centred),
        new(d => Invariant(d.Quantity), Centred),
        new(d => d.Rotated ? bool.TrueString : bool.FalseString, Centred),
        new(d => d.EdgeBandingMaterial, Centred),
        new(d => d.Note, Left),
    ];

    public IEnumerable<Cell> GetTableRow(IKroikoDetail detail, int rowNumber, int startColumnNumber) =>
        Row(Columns, (MegaTradingDetail)detail, rowNumber, startColumnNumber);
}