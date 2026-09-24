using System.Globalization;
using System.Text;

namespace Kroiko.Domain.CellsExtracting;

/// <summary>
/// Turns a Polyboard cut-list export (one <c>;</c>-separated Detail per line, 11 or 23 fields) into
/// Details. Bad lines are reported in <see cref="ParseResult.Errors"/>, never discarded or logged:
/// the caller decides what to do with them (ADR-0004 §3, ADR-0006 §2).
/// </summary>
public static class PolyboardParser
{
    private const char FieldSeparator = ';';
    private const int OldFormatFieldCount = 11;
    private const int LatestFormatFieldCount = 23;

    public static ParseResult Parse(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var lines = Encoding.UTF8.GetString(content).Split("\r\n");
        var details = new List<Detail>();
        var errors = new List<ParseError>();
        for (var i = 0; i < lines.Length; i++)
        {
            if (string.IsNullOrEmpty(lines[i]))
            {
                continue;
            }

            var lineNumber = i + 1;
            var fields = new FieldReader(lines[i].Split(FieldSeparator));
            switch (fields.Count)
            {
                case OldFormatFieldCount or LatestFormatFieldCount:
                    var detail = fields.Count == OldFormatFieldCount ? ParseOldFormat(fields) : ParseLatestFormat(fields);
                    if (fields.InvalidField is { } invalidField)
                    {
                        errors.Add(ParseError.InvalidNumber(lineNumber, invalidField));
                    }
                    else
                    {
                        details.Add(detail);
                    }

                    break;
                default:
                    errors.Add(ParseError.WrongFieldCount(lineNumber, fields.Count));
                    break;
            }
        }

        return new ParseResult(details, errors);
    }

    // The latest 23-field format: the legacy 11 fields, then thicknesses, reference, edge materials, oversizing.
    private static Detail ParseLatestFormat(FieldReader fields) => ParseOldFormat(fields) with
    {
        MaterialThickness = fields.Number(11, nameof(Detail.MaterialThickness)),
        TopEdgeThickness = fields.Number(12, nameof(Detail.TopEdgeThickness)),
        BottomEdgeThickness = fields.Number(13, nameof(Detail.BottomEdgeThickness)),
        RightEdgeThickness = fields.Number(14, nameof(Detail.RightEdgeThickness)),
        LeftEdgeThickness = fields.Number(15, nameof(Detail.LeftEdgeThickness)),
        Reference = fields.Text(16),
        TopEdgeMaterial = fields.Text(17),
        BottomEdgeMaterial = fields.Text(18),
        RightEdgeMaterial = fields.Text(19),
        LeftEdgeMaterial = fields.Text(20),
        OversizingHeight = fields.Number(21, nameof(Detail.OversizingHeight)),
        OversizingWidth = fields.Number(22, nameof(Detail.OversizingWidth)),
    };

    // The legacy 11-field format: the latest format's extra fields are 0 or empty.
    private static Detail ParseOldFormat(FieldReader fields) => new(
        fields.Number(0, nameof(Detail.Height)),
        fields.Number(1, nameof(Detail.Width)),
        fields.Integer(2, nameof(Detail.Quantity)),
        fields.Text(3),
        fields.Flag(4, nameof(Detail.IsGrainDirectionReversed)),
        fields.Flag(5, nameof(Detail.HasTopEdge)),
        fields.Flag(6, nameof(Detail.HasBottomEdge)),
        fields.Flag(7, nameof(Detail.HasRightEdge)),
        fields.Flag(8, nameof(Detail.HasLeftEdge)),
        fields.Text(9),
        fields.Integer(10, nameof(Detail.CuttingNumber)),
        0, 0, 0, 0, 0, "", "", "", "", "", 0, 0);

    /// <summary>
    /// Reads one line's fields as today's Server did: numbers in the invariant culture (a <c>,</c> is a
    /// thousands separator), an empty numeric field is <c>0</c>. The first field that is not a number is
    /// kept in <see cref="InvalidField"/> and read as <c>0</c>, so the caller can report the line.
    /// </summary>
    private sealed class FieldReader(string[] fields)
    {
        public int Count => fields.Length;

        /// <summary>The <see cref="Detail"/> property name of the first field that is not a number, if any.</summary>
        public string? InvalidField { get; private set; }

        public string Text(int index) => fields[index];

        public double Number(int index, string field) =>
            double.TryParse(OrZero(fields[index]), NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var value)
                ? value
                : Invalid<double>(field);

        public int Integer(int index, string field) =>
            int.TryParse(OrZero(fields[index]), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                ? value
                : Invalid<int>(field);

        // Polyboard writes 1 for "yes"; any other number is "no".
        public bool Flag(int index, string field) => Integer(index, field) == 1;

        private T Invalid<T>(string field) where T : struct
        {
            InvalidField ??= field;
            return default;
        }

        private static string OrZero(string value) => string.IsNullOrEmpty(value) ? "0" : value;
    }
}
