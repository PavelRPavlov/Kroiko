using System.Globalization;
using Kroiko.Domain.CellsExtracting;

namespace Kroiko.Client.Blazor.Conversion;

/// <summary>
/// The Bulgarian lines the upload panel shows for a rejected Polyboard file (ADR-0006 §2): the first
/// <see cref="MaxListed"/> bad lines as <c>ред N: …</c>, then <c>…и още N</c> for the rest.
/// </summary>
internal static class UploadErrorText
{
    public const int MaxListed = 10;

    public const string EmptyFile = "Файлът не съдържа детайли.";

    // The Detail property of a bad number, as the operator knows the column.
    private static readonly Dictionary<string, string> FieldNames = new()
    {
        [nameof(Detail.Height)] = "дължина",
        [nameof(Detail.Width)] = "ширина",
        [nameof(Detail.Quantity)] = "брой",
        [nameof(Detail.IsGrainDirectionReversed)] = "ротация",
        [nameof(Detail.HasTopEdge)] = "кант горе",
        [nameof(Detail.HasBottomEdge)] = "кант долу",
        [nameof(Detail.HasRightEdge)] = "кант дясно",
        [nameof(Detail.HasLeftEdge)] = "кант ляво",
        [nameof(Detail.CuttingNumber)] = "номер на детайла",
        [nameof(Detail.MaterialThickness)] = "дебелина на материала",
        [nameof(Detail.TopEdgeThickness)] = "дебелина на кант горе",
        [nameof(Detail.BottomEdgeThickness)] = "дебелина на кант долу",
        [nameof(Detail.RightEdgeThickness)] = "дебелина на кант дясно",
        [nameof(Detail.LeftEdgeThickness)] = "дебелина на кант ляво",
        [nameof(Detail.OversizingHeight)] = "надмярка по дължина",
        [nameof(Detail.OversizingWidth)] = "надмярка по ширина",
    };

    public static IReadOnlyList<string> Describe(IReadOnlyList<ParseError> errors)
    {
        var lines = errors.Take(MaxListed).Select(Describe).ToList();
        if (errors.Count > MaxListed)
        {
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"…и още {errors.Count - MaxListed}"));
        }

        return lines;
    }

    private static string Describe(ParseError error)
    {
        var reason = error.Kind switch
        {
            ParseErrorKind.FieldCount => string.Create(
                CultureInfo.InvariantCulture, $"{error.FieldCount} полета, очакват се 11 или 23"),
            ParseErrorKind.InvalidNumber => $"полето „{FieldName(error.Field)}“ не е число",
            _ => "неразпознат ред",
        };

        return string.Create(CultureInfo.InvariantCulture, $"ред {error.LineNumber}: {reason}");
    }

    private static string? FieldName(string? field) =>
        field is not null && FieldNames.TryGetValue(field, out var name) ? name : field;
}
