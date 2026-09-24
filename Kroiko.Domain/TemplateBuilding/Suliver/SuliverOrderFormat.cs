using System.Globalization;
using System.Text;
using Kroiko.Domain.CellsExtracting;

namespace Kroiko.Domain.TemplateBuilding.Suliver;

/// <summary>Suliver: every part in one <c>.xlsx</c>, with the edges, FALC and notes Suliver expects.</summary>
internal sealed class SuliverOrderFormat()
    : OrderFormatBase(new SuliverTemplateBuilder(new SuliverTableRowProvider()), new SuliverFileNameProvider())
{
    private const string NutLamEdgeFlagName = "nutlam";
    private const string NutEdgeFlagName = "nut";
    private const string NutBlumEdgeFlagName = "nutblum";
    private const string FalcEdgeFlagName = "falc";
    private const string DifferentEdgeMaterialName = "different";

    public override SupportedCompany Company => SupportedCompanies.Suliver;

    public override IReadOnlyList<KroikoFile> CreateFiles(IReadOnlyList<Detail> details) =>
        details.Count == 0
            ? []
            // TODO what is the required file name
            : [new KroikoFile { FileName = "Suliver", Details = details.Select(ToSuliverDetail).ToList() }];

    private static IKroikoDetail ToSuliverDetail(Detail detail)
    {
        var d = new SuliverDetail
        {
            Id = Guid.NewGuid(),
            Width = detail.Width,
            Height = detail.Height,
            Quantity = detail.Quantity,
            Material = detail.Material,
            Cabinet = $"{detail.Cabinet} {detail.Reference}",
            MaterialThickness = detail.MaterialThickness,
            IsGrainDirectionReversed = detail.IsGrainDirectionReversed ? (byte)2 : (byte)1,
        };
        SetSaliverEdges(d, detail);
        CreateSaliverNote(d, detail);
        return d;
    }

    private static void SetSaliverEdges(SuliverDetail suliverDetail, Detail detail)
    {
        if (detail.Width > detail.Height)
        {
            var result = GetSuliverEdgeThicknessValue(detail.TopEdgeThickness, detail.TopEdgeMaterial.ToLowerInvariant());
            suliverDetail.LongEdge2 = result.Value;
            suliverDetail.AdjustHeightForFalc(result.ShouldUpdateSize);

            result = GetSuliverEdgeThicknessValue(detail.BottomEdgeThickness, detail.BottomEdgeMaterial.ToLowerInvariant());
            suliverDetail.LongEdge = result.Value;
            suliverDetail.AdjustHeightForFalc(result.ShouldUpdateSize);

            result = GetSuliverEdgeThicknessValue(detail.LeftEdgeThickness, detail.LeftEdgeMaterial.ToLowerInvariant());
            suliverDetail.ShortEdge2 = result.Value;
            suliverDetail.AdjustWidthForFalc(result.ShouldUpdateSize);

            result = GetSuliverEdgeThicknessValue(detail.RightEdgeThickness, detail.RightEdgeMaterial.ToLowerInvariant());
            suliverDetail.ShortEdge = result.Value;
            suliverDetail.AdjustWidthForFalc(result.ShouldUpdateSize);
        }
        else
        {
            var result = GetSuliverEdgeThicknessValue(detail.LeftEdgeThickness, detail.LeftEdgeMaterial.ToLowerInvariant());
            suliverDetail.LongEdge2 = result.Value;
            suliverDetail.AdjustWidthForFalc(result.ShouldUpdateSize);

            result = GetSuliverEdgeThicknessValue(detail.RightEdgeThickness, detail.RightEdgeMaterial.ToLowerInvariant());
            suliverDetail.LongEdge = result.Value;
            suliverDetail.AdjustWidthForFalc(result.ShouldUpdateSize);

            result = GetSuliverEdgeThicknessValue(detail.TopEdgeThickness, detail.TopEdgeMaterial.ToLowerInvariant());
            suliverDetail.ShortEdge2 = result.Value;
            suliverDetail.AdjustHeightForFalc(result.ShouldUpdateSize);

            result = GetSuliverEdgeThicknessValue(detail.BottomEdgeThickness, detail.BottomEdgeMaterial.ToLowerInvariant());
            suliverDetail.ShortEdge = result.Value;
            suliverDetail.AdjustHeightForFalc(result.ShouldUpdateSize);
        }
    }

    private static void CreateSaliverNote(SuliverDetail suliverDetail, Detail detail)
    {
        var note = new StringBuilder();
        if (detail.OversizingHeight.Equals(detail.OversizingWidth) && detail.OversizingHeight > 0)
        {
            note.Append(CultureInfo.InvariantCulture, $"СДВ с краен размер {suliverDetail.Height - detail.OversizingHeight}x{suliverDetail.Width - detail.OversizingWidth}; ");
        }

        if (detail.TopEdgeMaterial.ToLowerInvariant().Contains(DifferentEdgeMaterialName) ||
            detail.BottomEdgeMaterial.ToLowerInvariant().Contains(DifferentEdgeMaterialName) ||
            detail.LeftEdgeMaterial.ToLowerInvariant().Contains(DifferentEdgeMaterialName) ||
            detail.RightEdgeMaterial.ToLowerInvariant().Contains(DifferentEdgeMaterialName))
        {
            note.Append("Кантиране с друг цвят");
            suliverDetail.IsEdgeColorDifferent = true;
        }

        suliverDetail.Note = note.ToString();
    }

    private static (string Value, bool ShouldUpdateSize) GetSuliverEdgeThicknessValue(double detailEdgeThickness, string detailEdgeMaterial)
    {
        if (detailEdgeMaterial.Contains(FalcEdgeFlagName))
        {
            return ("Фалц 13x4", true);
        }
        if (detailEdgeMaterial.Contains(NutLamEdgeFlagName))
        {
            return ("Нут Лам", false);
        }
        if (detailEdgeMaterial.Contains(NutBlumEdgeFlagName))
        {
            return ("Нут Блум", false);
        }
        if (detailEdgeMaterial.Contains(NutEdgeFlagName))
        {
            //NOTE this is the most generic name, so it should be last to give the other flags a chance to match
            return ("Нут 10x4", false);
        }
        return detailEdgeThickness switch
        {
            0 => ("0", false),
            > 0.4 and < 0.6 => ("1", false),
            > 0.7 and < 1.4 => ("3", false),
            > 1.6 and < 2.4 => ("2", false),
            _ => throw new ArgumentOutOfRangeException(nameof(detailEdgeThickness))
        };
    }
}
