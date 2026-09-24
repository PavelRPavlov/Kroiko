namespace Kroiko.Domain;

/// <summary>
/// Makes an order file's name one that a folder accepts and a browser download keeps as it is (ADR-0003 §7).
/// The names come from free text (materials, the customer's company name). Only the PWA's save layer calls
/// this, before it adds a <c> (n)</c> suffix; the Server's names are left as they are.
/// </summary>
public static class FileNameSanitizer
{
    private const char Replacement = '_';

    // "Order": the name when nothing of the original is left.
    private const string Fallback = "поръчка";

    private static readonly char[] IllegalCharacters = ['\\', '/', ':', '*', '?', '"', '<', '>', '|'];

    private static readonly HashSet<string> ReservedNames = new(
    [
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    ], StringComparer.OrdinalIgnoreCase);

    /// <summary>The safe form of <paramref name="fileName"/>.</summary>
    public static string Sanitize(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);

        // Windows drops trailing dots and spaces from a name, so a folder would not keep them.
        var name = ReplaceIllegalCharacters(fileName).TrimEnd('.', ' ');

        // The extension is everything from the last dot. A name that is only dots and spaces before it is no name.
        var lastDot = name.LastIndexOf('.');
        var stem = lastDot < 0 ? name : name[..lastDot];
        if (stem.Trim('.', ' ').Length == 0)
        {
            return Fallback + (lastDot < 0 ? string.Empty : name[lastDot..]);
        }

        return IsReservedName(name) ? Replacement + name : name;
    }

    // Windows reads a name as a device when the part before its first dot, trailing spaces aside, is a
    // reserved name in any case: CON, CON.xlsx and "CON .xlsx" all are.
    private static bool IsReservedName(string name)
    {
        var firstDot = name.IndexOf('.');
        var device = (firstDot < 0 ? name : name[..firstDot]).TrimEnd(' ');
        return ReservedNames.Contains(device);
    }

    private static string ReplaceIllegalCharacters(string fileName) =>
        string.Create(fileName.Length, fileName, static (chars, name) =>
        {
            for (var i = 0; i < name.Length; i++)
            {
                chars[i] = IsIllegal(name[i]) ? Replacement : name[i];
            }
        });

    // Control characters are U+0000–U+001F and U+007F–U+009F.
    private static bool IsIllegal(char c) => char.IsControl(c) || Array.IndexOf(IllegalCharacters, c) >= 0;
}
