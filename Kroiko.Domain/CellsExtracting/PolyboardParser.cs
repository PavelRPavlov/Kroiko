using System.Globalization;
using System.Text;

namespace Kroiko.Domain.CellsExtracting;

/// <summary>
/// Turns a Polyboard cut-list export (one <c>;</c>-separated Detail per line, 11 or 23 fields) into
/// Details. Bad lines are reported in <see cref="ParseResult.Errors"/>, never discarded or logged:
/// the caller decides what to do with them (ADR-0004 §3, ADR-0006 §2). Lines may end in CRLF, LF or CR,
/// a leading UTF-8 BOM is ignored, and empty or whitespace-only lines are skipped (ADR-0006 §1); a
/// <see cref="ParseError.LineNumber"/> still counts every physical line.
/// </summary>
public static class PolyboardParser
{
    private const char FieldSeparator = ';';
    private const int OldFormatFieldCount = 11;
    private const int LatestFormatFieldCount = 23;

    // Tried in this order at each position, so "\r\n" is one line ending, not two.
    private static readonly string[] LineEndings = ["\r\n", "\n", "\r"];

    public static ParseResult Parse(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var lines = Decode(content).Split(LineEndings, StringSplitOptions.None);
        var details = new List<Detail>();
        var errors = new List<ParseError>();
        for (var i = 0; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
            {
                continue;
            }

            var lineNumber = i + 1;
            var fields = new FieldReader(lines[i].Split(FieldSeparator));
            var detail = fields.Count switch
            {
                OldFormatFieldCount => ParseOldFormat(fields),
                LatestFormatFieldCount => ParseLatestFormat(fields),
                _ => null,
            };

            if (detail is null)
            {
                errors.Add(new ParseError(lineNumber, ParseErrorKind.FieldCount, FieldCount: fields.Count));
            }
            else if (fields.InvalidField is { } invalidField)
            {
                errors.Add(new ParseError(lineNumber, ParseErrorKind.InvalidNumber, Field: invalidField));
            }
            else
            {
                details.Add(detail);
            }
        }

        return new ParseResult(details, errors);
    }

    // UTF-8 (ADR-0006 §5). GetString keeps a BOM as U+FEFF, which would break the first line's first number.
    private static string Decode(byte[] content)
    {
        var bom = Encoding.UTF8.Preamble;
        return content.AsSpan().StartsWith(bom)
            ? Encoding.UTF8.GetString(content, bom.Length, content.Length - bom.Length)
            : Encoding.UTF8.GetString(content);
    }

    // The latest 23-field format: the legacy 11 fields, then (initializers run in order, after them)
    // thicknesses, reference, edge materials, oversizing.
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
    // The fields are read in column order, so FieldReader.InvalidField is the line's first bad field.
    private static Detail ParseOldFormat(FieldReader fields) => new(
        Height: fields.Number(0, nameof(Detail.Height)),
        Width: fields.Number(1, nameof(Detail.Width)),
        Quantity: fields.Integer(2, nameof(Detail.Quantity)),
        Material: fields.Text(3),
        IsGrainDirectionReversed: fields.Flag(4, nameof(Detail.IsGrainDirectionReversed)),
        HasTopEdge: fields.Flag(5, nameof(Detail.HasTopEdge)),
        HasBottomEdge: fields.Flag(6, nameof(Detail.HasBottomEdge)),
        HasRightEdge: fields.Flag(7, nameof(Detail.HasRightEdge)),
        HasLeftEdge: fields.Flag(8, nameof(Detail.HasLeftEdge)),
        Cabinet: fields.Text(9),
        CuttingNumber: fields.Integer(10, nameof(Detail.CuttingNumber)),
        MaterialThickness: 0,
        TopEdgeThickness: 0,
        BottomEdgeThickness: 0,
        RightEdgeThickness: 0,
        LeftEdgeThickness: 0,
        Reference: "",
        TopEdgeMaterial: "",
        BottomEdgeMaterial: "",
        RightEdgeMaterial: "",
        LeftEdgeMaterial: "",
        OversizingHeight: 0,
        OversizingWidth: 0);

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
