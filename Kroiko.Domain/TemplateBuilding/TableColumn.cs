using System.Globalization;

namespace Kroiko.Domain.TemplateBuilding;

/// <summary>
/// One column of a manufacturer's detail table (ADR-0004 §5): the cell value a detail writes, and
/// the cell's content alignment (<see cref="TableColumn.Left"/> or <see cref="TableColumn.Centred"/>).
/// A row provider lists its columns in order, explicitly, instead of looking properties up by name.
/// </summary>
internal sealed record TableColumn<TDetail>(Func<TDetail, string?> Value, byte ContentAlignment);

internal static class TableColumn
{
    /// <summary>Written as it is (text).</summary>
    public const byte Left = 0;

    /// <summary>Centred, and written as a number when the value is one (see <c>ExcelFileGenerator</c>).</summary>
    public const byte Centred = 1;

    // Numbers are written in the invariant culture (ADR-0004 §4), as ExcelFileGenerator reads them back.
    public static string Invariant<T>(T value) where T : IFormattable => value.ToString(null, CultureInfo.InvariantCulture);

    /// <summary>
    /// The cells <paramref name="detail"/> writes into row <paramref name="rowNumber"/>, one per column,
    /// from column <paramref name="startColumnNumber"/> on.
    /// </summary>
    public static List<Cell> Row<TDetail>(
        IReadOnlyList<TableColumn<TDetail>> columns, TDetail detail, int rowNumber, int startColumnNumber)
    {
        var result = new List<Cell>(columns.Count);
        for (var i = 0; i < columns.Count; i++)
        {
            var column = columns[i];
            result.Add(new Cell(Cell.GetCellName(rowNumber, startColumnNumber + i), column.ContentAlignment)
            {
                Value = column.Value(detail),
            });
        }

        return result;
    }
}
