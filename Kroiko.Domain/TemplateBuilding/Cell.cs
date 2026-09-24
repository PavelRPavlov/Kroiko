using System.Globalization;
using System.Text;

namespace Kroiko.Domain.TemplateBuilding;

public record Cell(string Name, byte ContentAlignment = 0)
{
    public static readonly Cell Empty = new(string.Empty);

    public string? Value { get; set; }
    
    public int Row => GetRowAndColumn(this).Row;

    public static (int Row, int Column) GetRowAndColumn(Cell cell)
    {
        var codes = Encoding.ASCII.GetBytes(cell.Name);
        var col = (codes[0] - 65) + 1;
        int row = 0;
        byte multiplier = 1;
        for (var i = codes.Length - 1; i > 0; i--)
        {
            row += ((codes[i] - 49) + 1) * multiplier;
            multiplier *= 10;
        }
        return (row, col);
    }

    public static string GetCellName(int row, int column)
    {
        var builder = new StringBuilder();
        var col = column;
        while (col > 0)
        {
            var remainder = col % 26;
            if (remainder == 0)
            {
                remainder = 26;
            }
            builder.Insert(0, (char)(remainder + 64));
            col = (col - remainder) / 26;
        }
        builder.Append(row.ToString(CultureInfo.InvariantCulture));
        return builder.ToString();
    }
}