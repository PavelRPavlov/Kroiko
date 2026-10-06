namespace Kroiko.Domain;

/// <summary>
/// A reason an order format refuses to generate, found by <see cref="IOrderFormat.Check"/> (ADR-0006 §3). The
/// PWA blocks generation while any exists and writes the Bulgarian text; the Server does not check.
/// </summary>
public abstract record OrderProblem;

/// <summary>
/// The files use more distinct materials than the format can list: MegaTrading's <c>.cut_mt</c> header has
/// room for <paramref name="Max"/>. <paramref name="Materials"/> are all of them, in order of first use.
/// </summary>
public sealed record TooManyMaterials(int Max, IReadOnlyList<string> Materials) : OrderProblem;

/// <summary>
/// MegaTrading parts have banded sides whose edge-banding width or thickness is not one MegaTrading's software accepts
/// (ADR-0015), usually because Polyboard does not give it. <paramref name="Materials"/> are the materials of those
/// parts, in order of first use. Unlike the other problems the operator resolves it while generating: the PWA asks for
/// the missing values per material and fills them in (<see cref="TemplateBuilding.MegaTradingEdges.FillMissing"/>).
/// </summary>
public sealed record MissingEdgeBanding(IReadOnlyList<MaterialEdgeBanding> Materials) : OrderProblem
{
    /// <summary>How many banded sides, over all the materials, miss a value.</summary>
    public int Edges => Materials.Sum(m => m.Edges);
}

/// <summary>
/// The banded sides of one material's parts that miss a value: <paramref name="Edges"/> of them, some missing the
/// width (<paramref name="NeedsWidth"/>), some the thickness (<paramref name="NeedsThickness"/>).
/// </summary>
public sealed record MaterialEdgeBanding(string Material, int Edges, bool NeedsWidth, bool NeedsThickness);
