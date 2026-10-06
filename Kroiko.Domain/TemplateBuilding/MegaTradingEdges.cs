namespace Kroiko.Domain.TemplateBuilding;

/// <summary>The four banded sides of the MegaTrading parts: what is missing, and filling it in (ADR-0015).</summary>
public static class MegaTradingEdges
{
    /// <summary>
    /// The banded sides of <paramref name="files"/>' parts that MegaTrading would not accept, by material in order of
    /// first use, or <c>null</c> when every banded side is complete.
    /// </summary>
    public static MissingEdgeBanding? FindMissing(IEnumerable<KroikoFile> files)
    {
        ArgumentNullException.ThrowIfNull(files);

        var materials = Details(files)
            .SelectMany(d => Edges(d).Select(edge => (d.Material, Edge: edge)))
            .Where(e => !e.Edge.IsComplete)
            .GroupBy(e => e.Material)
            .Select(g => new MaterialEdgeBanding(
                g.Key, g.Count(), g.Any(e => !e.Edge.HasWidth), g.Any(e => !e.Edge.HasThickness)))
            .ToList();
        return materials.Count == 0 ? null : new MissingEdgeBanding(materials);
    }

    /// <summary>
    /// Completes every banded side of <paramref name="files"/>' parts whose material is a key of
    /// <paramref name="picks"/> with that pick (<see cref="MegaTradingEdge.CompletedWith"/>): only the values
    /// MegaTrading would not accept change, so values the operator chose in the grid stay. Sides with no edge stay
    /// without one.
    /// </summary>
    /// <returns><c>true</c> when any side changed.</returns>
    public static bool FillMissing(IEnumerable<KroikoFile> files, IReadOnlyDictionary<string, MegaTradingEdge> picks)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(picks);

        var changed = false;
        foreach (var detail in Details(files))
        {
            if (detail.Material is null || !picks.TryGetValue(detail.Material, out var pick))
            {
                continue;
            }

            detail.LeftEdge = Complete(detail.LeftEdge, pick, ref changed);
            detail.BottomEdge = Complete(detail.BottomEdge, pick, ref changed);
            detail.RightEdge = Complete(detail.RightEdge, pick, ref changed);
            detail.TopEdge = Complete(detail.TopEdge, pick, ref changed);
        }

        return changed;
    }

    private static IEnumerable<MegaTradingDetail> Details(IEnumerable<KroikoFile> files) =>
        files.SelectMany(f => f.Details).OfType<MegaTradingDetail>();

    // The banded sides of a part; a side with no edge is not one.
    private static IEnumerable<MegaTradingEdge> Edges(MegaTradingDetail detail) =>
        new[] { detail.LeftEdge, detail.BottomEdge, detail.RightEdge, detail.TopEdge }
            .Select(MegaTradingEdge.Parse)
            .OfType<MegaTradingEdge>();

    private static string Complete(string cell, MegaTradingEdge pick, ref bool changed)
    {
        if (MegaTradingEdge.Parse(cell) is not { IsComplete: false } edge)
        {
            return cell;
        }

        var completed = edge.CompletedWith(pick).ToString();
        changed |= completed != cell;
        return completed;
    }
}
