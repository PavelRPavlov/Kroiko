using System.Globalization;
using Kroiko.Domain.CellsExtracting;

namespace Kroiko.Domain.TemplateBuilding.Lonira;

/// <summary>Lonira: one <c>.xlsx</c> per material, named after it.</summary>
internal sealed class LoniraOrderFormat()
    : OrderFormatBase(new LoniraTemplateBuilder(new LoniraTableRowProvider()), new LoniraFileNameProvider())
{
    public override SupportedCompany Company => SupportedCompanies.Lonira;

    // One file per material, in order of first use; parts without a material are left out.
    public override IReadOnlyList<KroikoFile> CreateFiles(IReadOnlyList<Detail> details) =>
        details.GroupBy(detail => detail.Material)
            .Where(group => !string.IsNullOrEmpty(group.Key))
            .Select(group => new KroikoFile { FileName = group.Key, Details = group.Select(ToLoniraDetail).ToList() })
            .ToList();

    private static IKroikoDetail ToLoniraDetail(Detail detail) => new LoniraDetail
    {
        Id = Guid.NewGuid(),
        Width = detail.Width,
        Height = detail.Height,
        Quantity = detail.Quantity,
        // Only the edging descriptor goes into the "Кантиране" column — shown in the review
        // grid and written to the generated file. (The cabinet/cutting-number suffix that
        // used to be appended here is intentionally dropped.)
        LoniraEdges = GetLoniraEdges(detail),
        Note = CreateLoniraNote(detail),
    };

    private static string CreateLoniraNote(Detail detail)
    {
        if (detail.OversizingHeight.Equals(detail.OversizingWidth) && detail.OversizingHeight > 0)
        {
            return string.Create(CultureInfo.InvariantCulture,
                $"СДВ с краен размер {detail.Height - detail.OversizingHeight}x{detail.Width - detail.OversizingWidth}; ");
        }

        return string.Empty;
    }

    private static string GetLoniraEdges(Detail detail)
    {
        if (detail.IsGrainDirectionReversed)
        {
            detail = detail with { Height = detail.Width, Width = detail.Height };
        }

        int longEdgeCount = 0;
        int shortEdgeCount = 0;

        if (detail.Width >= detail.Height)
        {
            if (detail.HasLeftEdge)
            {
                shortEdgeCount++;
            }

            if (detail.HasTopEdge)
            {
                longEdgeCount++;
            }

            if (detail.HasRightEdge)
            {
                shortEdgeCount++;
            }

            if (detail.HasBottomEdge)
            {
                longEdgeCount++;
            }
        }
        else
        {
            if (detail.HasLeftEdge)
            {
                longEdgeCount++;
            }

            if (detail.HasTopEdge)
            {
                shortEdgeCount++;
            }

            if (detail.HasRightEdge)
            {
                longEdgeCount++;
            }

            if (detail.HasBottomEdge)
            {
                shortEdgeCount++;
            }
        }

        if (shortEdgeCount == 0 && longEdgeCount == 0)
        {
            return string.Empty;
        }

        if (shortEdgeCount == 0 && longEdgeCount != 0)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{longEdgeCount} d");
        }

        if (longEdgeCount == 0 && shortEdgeCount != 0)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{shortEdgeCount} k");
        }

        return string.Create(CultureInfo.InvariantCulture, $"{shortEdgeCount} k {longEdgeCount} d");
    }
}
