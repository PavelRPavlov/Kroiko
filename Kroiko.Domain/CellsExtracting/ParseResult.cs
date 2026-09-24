namespace Kroiko.Domain.CellsExtracting;

/// <summary>
/// What <see cref="PolyboardParser.Parse"/> found: the Details of every good line and one
/// <see cref="ParseError"/> per bad line, in file order.
/// </summary>
public sealed record ParseResult(IReadOnlyList<Detail> Details, IReadOnlyList<ParseError> Errors);
