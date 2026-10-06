using Kroiko.Domain;
using Kroiko.Domain.TemplateBuilding;

namespace Kroiko.Client.Blazor.Conversion;

/// <summary>
/// Asks the operator, before <see cref="ConverterState"/> generates a MegaTrading order, for the edge-banding width and
/// thickness Polyboard did not give (ADR-0015). The app asks with a Bulgarian dialog; the tests fake it (ADR-0007 §4).
/// </summary>
public interface IEdgeBandingPrompt
{
    /// <summary>
    /// Shows <paramref name="missing"/>'s materials and asks, per material, for what its banded sides miss: a width,
    /// a thickness or both. Returns the picks by material (a part the material did not need is empty), or <c>null</c>
    /// when the operator cancels.
    /// </summary>
    Task<IReadOnlyDictionary<string, MegaTradingEdge>?> AskAsync(MissingEdgeBanding missing);
}
