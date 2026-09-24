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
    // merges two materials frees a header row.
    public override IReadOnlyList<OrderProblem> Check(IReadOnlyList<KroikoFile> files)
    {
        ArgumentNullException.ThrowIfNull(files);

        var materials = MegaTradingFileGenerator.GroupByMaterial(files).Select(g => g.Key).ToList();
        return materials.Count > MegaTradingFileGenerator.MaxMaterials
            ? [new TooManyMaterials(MegaTradingFileGenerator.MaxMaterials, materials)]
            : [];
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
        // TODO edge material should hold the overall thickness of the edge banding (e.g. 22,28 or 42)
        RightEdge = detail.HasTopEdge ? $"{detail.TopEdgeMaterial}/{GetEdgeBandingThickness(detail.TopEdgeThickness)}" : string.Empty,
        TopEdge = detail.HasLeftEdge ? $"{detail.LeftEdgeMaterial}/{GetEdgeBandingThickness(detail.LeftEdgeThickness)}" : string.Empty,
        BottomEdge = detail.HasRightEdge ? $"{detail.RightEdgeMaterial}/{GetEdgeBandingThickness(detail.RightEdgeThickness)}" : string.Empty,
        LeftEdge = detail.HasBottomEdge ? $"{detail.BottomEdgeMaterial}/{GetEdgeBandingThickness(detail.BottomEdgeThickness)}" : string.Empty,
    };

    private static string HasAnyEdgeSet(Detail detail) =>
        detail.HasBottomEdge || detail.HasLeftEdge || detail.HasRightEdge || detail.HasTopEdge ? detail.Material : string.Empty;

    private static string GetEdgeBandingThickness(double detailLeftEdgeThickness) =>
        detailLeftEdgeThickness switch
        {
            <= 0.5 => "0.5",
            > 0.5 and <= 1 => "0.8/1.0",
            >= 1 => "2.0",
            _ => ""
        };
}
