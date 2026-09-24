using Kroiko.Domain.CellsExtracting;

namespace Kroiko.Domain.Tests;

/// <summary>Parsed Details and format lookups shared by the order-format tests.</summary>
internal static class FormatTestData
{
    /// <summary>A plain 18 mm part of <paramref name="material"/>, with no edges and no oversizing.</summary>
    public static Detail Part(string material, double height = 600, double width = 300) => new(
        Height: height, Width: width, Quantity: 1, Material: material, IsGrainDirectionReversed: false,
        HasTopEdge: false, HasBottomEdge: false, HasRightEdge: false, HasLeftEdge: false,
        Cabinet: "Шкаф", CuttingNumber: 1, MaterialThickness: 18,
        TopEdgeThickness: 0, BottomEdgeThickness: 0, RightEdgeThickness: 0, LeftEdgeThickness: 0,
        Reference: "1", TopEdgeMaterial: "", BottomEdgeMaterial: "", RightEdgeMaterial: "", LeftEdgeMaterial: "",
        OversizingHeight: 0, OversizingWidth: 0);

    /// <summary>One part per material, in order.</summary>
    public static Detail[] PartsOf(params string[] materials) => materials.Select(m => Part(m)).ToArray();

    // Theories name the manufacturer, as InlineData cannot hold a SupportedCompany.
    public static IOrderFormat FormatNamed(string manufacturer) =>
        OrderFormats.For(new[] { SupportedCompanies.Lonira, SupportedCompanies.Suliver, SupportedCompanies.MegaTrading }
            .Single(c => c.Name == manufacturer));
}
