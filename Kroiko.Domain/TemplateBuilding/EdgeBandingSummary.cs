using System.Globalization;

namespace Kroiko.Domain.TemplateBuilding;

/// <summary>
/// How many short (<c>k</c>) and long (<c>d</c>) sides of a part are edge-banded, as Lonira's "Кантиране" column and
/// MegaTrading's "Забележка" column show it: <c>2 k 2 d</c>, <c>1 k</c>, <c>2 d</c>, or empty with no edges.
/// </summary>
internal static class EdgeBandingSummary
{
    /// <summary>
    /// The summary for a part of <paramref name="height"/> × <paramref name="width"/> whose Polyboard sides are banded as
    /// given. A part with its grain reversed is turned first, so its sides are counted along the grain.
    /// </summary>
    public static string Describe(
        double height, double width, bool isGrainDirectionReversed, bool top, bool bottom, bool right, bool left)
    {
        if (isGrainDirectionReversed)
        {
            (height, width) = (width, height);
        }

        int longEdgeCount = 0;
        int shortEdgeCount = 0;

        if (width >= height)
        {
            if (left)
            {
                shortEdgeCount++;
            }

            if (top)
            {
                longEdgeCount++;
            }

            if (right)
            {
                shortEdgeCount++;
            }

            if (bottom)
            {
                longEdgeCount++;
            }
        }
        else
        {
            if (left)
            {
                longEdgeCount++;
            }

            if (top)
            {
                shortEdgeCount++;
            }

            if (right)
            {
                longEdgeCount++;
            }

            if (bottom)
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
