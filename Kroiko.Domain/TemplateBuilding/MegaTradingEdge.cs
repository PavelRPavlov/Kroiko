namespace Kroiko.Domain.TemplateBuilding;

/// <summary>
/// One banded side of a MegaTrading part, as its <c>.cut_mt</c> cell holds it: <c>{width}/{thickness}</c>, e.g.
/// <c>22/0.5</c> (ADR-0015). MegaTrading's software opens the file into a grid whose edge cells are dropdowns of
/// exactly <see cref="Widths"/> × <see cref="Thicknesses"/>; any other value is an error there. Polyboard gives the
/// thickness at most, never the width, so the part it does not give is empty (<c>/0.5</c>, <c>/</c>) until the
/// operator picks it. A side with no edge is the empty string, not an edge.
/// </summary>
public readonly record struct MegaTradingEdge(string Width, string Thickness)
{
    private const char Separator = '/';

    /// <summary>The edge-banding widths (mm) MegaTrading accepts: the band is wider than the panel it covers.</summary>
    public static IReadOnlyList<string> Widths { get; } = ["22", "28", "42"];

    /// <summary>The edge-banding thicknesses (mm) MegaTrading accepts.</summary>
    public static IReadOnlyList<string> Thicknesses { get; } = ["0.5", "0.8/1.0", "2.0"];

    /// <summary>The width is one MegaTrading accepts.</summary>
    public bool HasWidth => Widths.Contains(Width);

    /// <summary>The thickness is one MegaTrading accepts.</summary>
    public bool HasThickness => Thicknesses.Contains(Thickness);

    /// <summary>MegaTrading's software accepts this edge as it is.</summary>
    public bool IsComplete => HasWidth && HasThickness;

    /// <summary>
    /// The edge in a <c>.cut_mt</c> cell, or <c>null</c> for a side with no edge (<c>null</c> or empty). The width is
    /// everything before the first <c>/</c>, the thickness everything after it, so <c>22/0.8/1.0</c> is 22 and
    /// 0.8/1.0; a value with no <c>/</c> is a width with no thickness.
    /// </summary>
    public static MegaTradingEdge? Parse(string? cell)
    {
        if (string.IsNullOrEmpty(cell))
        {
            return null;
        }

        var separator = cell.IndexOf(Separator);
        return separator < 0
            ? new MegaTradingEdge(cell, string.Empty)
            : new MegaTradingEdge(cell[..separator], cell[(separator + 1)..]);
    }

    /// <summary>
    /// The thickness MegaTrading lists for a Polyboard edge thickness in mm, or empty when Polyboard gave none
    /// (0: the 11-field format has no thickness, and the 23-field one leaves the field empty).
    /// </summary>
    public static string ThicknessFromPolyboard(double thickness) => thickness switch
    {
        <= 0 => string.Empty,
        <= 0.5 => "0.5",
        <= 1 => "0.8/1.0",
        _ => "2.0",
    };

    /// <summary>
    /// This edge with each part that MegaTrading would not accept replaced by <paramref name="pick"/>'s part; the parts
    /// it accepts already stay, and so does a part <paramref name="pick"/> leaves empty.
    /// </summary>
    public MegaTradingEdge CompletedWith(MegaTradingEdge pick) => new(
        HasWidth || string.IsNullOrEmpty(pick.Width) ? Width : pick.Width,
        HasThickness || string.IsNullOrEmpty(pick.Thickness) ? Thickness : pick.Thickness);

    /// <summary>The <c>.cut_mt</c> cell: <c>{width}/{thickness}</c>.</summary>
    public override string ToString() => $"{Width}{Separator}{Thickness}";
}
