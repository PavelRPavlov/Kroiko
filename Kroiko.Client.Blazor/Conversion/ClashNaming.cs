using System.Globalization;

namespace Kroiko.Client.Blazor.Conversion;

/// <summary>
/// The names "Запази в папка…" writes the order files under, so that nothing is overwritten (ADR-0003 §4): a name
/// already in the folder, or already given to an earlier file of the same save, gets <c> (2)</c>, <c> (3)</c>, …
/// before its extension (<c>Egger W1000 (2).xlsx</c>). The names it gets are already <see cref="Kroiko.Domain.FileNameSanitizer"/>
/// names (ADR-0003 §7), and a number keeps them safe.
/// </summary>
public static class ClashNaming
{
    /// <summary>
    /// The final name of each of <paramref name="targetNames"/>, in the same order: the name itself when it is free,
    /// otherwise the name with the first free number from 2. Names are compared ignoring case, as a Windows folder does.
    /// </summary>
    /// <param name="targetNames">The sanitised names of the files to save.</param>
    /// <param name="existingNames">The names already in the folder.</param>
    public static IReadOnlyList<string> FinalNames(IReadOnlyList<string> targetNames, IEnumerable<string> existingNames)
    {
        ArgumentNullException.ThrowIfNull(targetNames);
        ArgumentNullException.ThrowIfNull(existingNames);

        var taken = new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);
        return targetNames.Select(name => FreeName(name, taken)).ToList();
    }

    // The first of name, "name (2)", "name (3)", … not yet taken, which it then takes.
    private static string FreeName(string name, HashSet<string> taken)
    {
        var finalName = name;
        for (var number = 2; taken.Contains(finalName); number++)
        {
            finalName = WithNumber(name, number);
        }

        taken.Add(finalName);
        return finalName;
    }

    // The extension is everything from the last dot, as FileNameSanitizer reads it.
    private static string WithNumber(string name, int number)
    {
        var lastDot = name.LastIndexOf('.');
        return lastDot < 0
            ? string.Create(CultureInfo.InvariantCulture, $"{name} ({number})")
            : string.Create(CultureInfo.InvariantCulture, $"{name[..lastDot]} ({number}){name[lastDot..]}");
    }
}
