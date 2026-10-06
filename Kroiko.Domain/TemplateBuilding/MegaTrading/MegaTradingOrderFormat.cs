using Kroiko.Domain.CellsExtracting;
using Kroiko.Domain.ExcelFilesGeneration;
using Kroiko.Domain.TextFileGeneration;

namespace Kroiko.Domain.TemplateBuilding.MegaTrading;

/// <summary>MegaTrading: every part in one <c>.cut_mt</c> text file and one <c>.xlsx</c>.</summary>
internal sealed class MegaTradingOrderFormat()
    : OrderFormatBase(new MegaTradingTemplateBuilder(new MegaTradingTableRowProvider()), new MegaTradingFileNameProvider())
{
    public override SupportedCompany Company => SupportedCompanies.MegaTrading;

    // TODO what is the required file name
    public override IReadOnlyList<KroikoFile> CreateFiles(IReadOnlyList<Detail> details) =>
        OneFile("MegaTrading", details, ToMegaTradingDetail);

    // Counts materials exactly as the .cut_mt header does, after any rename the operator made, so a rename that
    // merges two materials frees a header row. Then the banded sides MegaTrading's software would not accept (ADR-0015).
    public override IReadOnlyList<OrderProblem> Check(IReadOnlyList<KroikoFile> files)
    {
        ArgumentNullException.ThrowIfNull(files);

        var problems = new List<OrderProblem>();
        var materials = MegaTradingFileGenerator.GroupByMaterial(files).Select(g => g.Key).ToList();
        if (materials.Count > MegaTradingFileGenerator.MaxMaterials)
        {
            problems.Add(new TooManyMaterials(MegaTradingFileGenerator.MaxMaterials, materials));
        }

        if (MegaTradingEdges.FindMissing(files) is { } missing)
        {
            problems.Add(missing);
        }

        return problems;
    }

    // The .cut_mt comes first, then the .xlsx.
    public override IReadOnlyList<FileSaveContext> Generate(ContactInfo contact, IReadOnlyList<KroikoFile> files, string? differentEdgeColor)
    {
        ArgumentNullException.ThrowIfNull(contact);
        ArgumentNullException.ThrowIfNull(files);

        return [.. MegaTradingFileGenerator.CreateTextBasedFile(contact, files), .. base.Generate(contact, files, differentEdgeColor)];
    }

    private static IKroikoDetail ToMegaTradingDetail(Detail detail) => new MegaTradingDetail
    {
        Id = Guid.NewGuid(),
        Width = detail.Width,
        Height = detail.Height,
        Quantity = detail.Quantity,
        Material = detail.Material,
        Thickness = detail.MaterialThickness,
        // TODO display warning in the UI if the edge materials differ in Polyboard
        EdgeBandingMaterial = HasAnyEdgeSet(detail),
        Rotated = detail.IsGrainDirectionReversed,
        Note = string.Empty,
        // Polyboard never gives the band's width and not always its thickness: what it does not give stays empty for the
        // operator to pick (ADR-0015). Polyboard's edge material ("Same", "Falc", …) is no value MegaTrading accepts.
        RightEdge = Edge(detail.HasTopEdge, detail.TopEdgeThickness),
        TopEdge = Edge(detail.HasLeftEdge, detail.LeftEdgeThickness),
        BottomEdge = Edge(detail.HasRightEdge, detail.RightEdgeThickness),
        LeftEdge = Edge(detail.HasBottomEdge, detail.BottomEdgeThickness),
    };

    private static string Edge(bool hasEdge, double thickness) =>
        hasEdge ? new MegaTradingEdge(string.Empty, MegaTradingEdge.ThicknessFromPolyboard(thickness)).ToString() : string.Empty;

    private static string HasAnyEdgeSet(Detail detail) =>
        detail.HasBottomEdge || detail.HasLeftEdge || detail.HasRightEdge || detail.HasTopEdge ? detail.Material : string.Empty;
}
