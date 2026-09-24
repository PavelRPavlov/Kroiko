namespace Kroiko.Domain.CellsExtracting;

/// <summary>Why <see cref="PolyboardParser"/> rejected a line.</summary>
public enum ParseErrorKind
{
    /// <summary>The line has neither 11 (legacy) nor 23 (latest) fields; see <see cref="ParseError.FieldCount"/>.</summary>
    FieldCount,

    /// <summary>A numeric field is not an invariant-culture number; see <see cref="ParseError.Field"/>.</summary>
    InvalidNumber,
}

/// <summary>
/// One bad line of a Polyboard file. Structured, so each app words it in its own language
/// (ADR-0006 §2): the 1-based <paramref name="LineNumber"/>, the <paramref name="Kind"/>, and the line's
/// <paramref name="FieldCount"/> or the <see cref="Detail"/> property name of the bad <paramref name="Field"/>.
/// </summary>
public sealed record ParseError(int LineNumber, ParseErrorKind Kind, int? FieldCount = null, string? Field = null)
{
    public static ParseError WrongFieldCount(int lineNumber, int fieldCount) =>
        new(lineNumber, ParseErrorKind.FieldCount, FieldCount: fieldCount);

    public static ParseError InvalidNumber(int lineNumber, string field) =>
        new(lineNumber, ParseErrorKind.InvalidNumber, Field: field);
}
