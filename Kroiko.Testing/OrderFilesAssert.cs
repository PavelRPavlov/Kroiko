using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Kroiko.Domain.ExcelFilesGeneration;

namespace Kroiko.Testing;

/// <summary>
/// Compares generated order files with the golden files in
/// <c>TestData/golden/&lt;fixture&gt;/&lt;manufacturer&gt;/</c> (ADR-0007 §2).
/// </summary>
public static class OrderFilesAssert
{
    private const string NamesFile = "names.txt";
    private const string DateToken = "{date}";

    // The one place that knows the generation-date format: the file-name providers write
    // DateTime.Now as yyyy-MM-dd (MegaTrading, Suliver). The .cut_mt header (its first line) carries
    // no date today; it is normalised the same way. The .cut_mt detail rows are never normalised,
    // because they hold free text from the Polyboard file.
    private static readonly Regex GenerationDate = new(@"(?<!\d)\d{4}-\d{2}-\d{2}(?!\d)", RegexOptions.CultureInvariant);

    /// <summary>
    /// Asserts that <paramref name="files"/>, in generation order, match the golden files recorded for
    /// <paramref name="fixture"/> and <paramref name="manufacturer"/>: the worksheet XML of every
    /// <c>.xlsx</c>, the bytes of every other file (a <c>.cut_mt</c>) and the file names in
    /// <c>names.txt</c>, with generation dates replaced by <c>{date}</c>.
    /// With the environment variable <c>UPDATE_GOLDEN=1</c> it instead records <paramref name="files"/>
    /// as the golden files, in the source tree, and passes.
    /// </summary>
    /// <param name="fixture">The Polyboard fixture name, e.g. <c>"cabinet-23-field"</c>; one folder name.</param>
    /// <param name="manufacturer">The golden folder for this run, e.g. <c>"Lonira"</c>; one folder name.</param>
    /// <param name="files">The generated order files, in generation order.</param>
    /// <exception cref="GoldenFileMismatchException">The files do not match.</exception>
    public static void MatchGolden(string fixture, string manufacturer, IReadOnlyList<FileSaveContext> files)
    {
        var update = Environment.GetEnvironmentVariable("UPDATE_GOLDEN") == "1";
        MatchGolden(fixture, manufacturer, files, update ? TestData.GoldenSourceRoot : TestData.GoldenRoot, update);
    }

    internal static void MatchGolden(
        string fixture, string manufacturer, IReadOnlyList<FileSaveContext> files, string goldenRoot, bool update)
    {
        // Recording deletes this folder, so neither argument may reach outside it.
        RequireFolderName(fixture, nameof(fixture));
        RequireFolderName(manufacturer, nameof(manufacturer));

        var golden = new GoldenSet(fixture, manufacturer, Path.Combine(goldenRoot, fixture, manufacturer));
        var generated = Normalise(files);

        if (update)
        {
            golden.Record(generated);
        }
        else
        {
            golden.Compare(generated);
        }
    }

    private static void RequireFolderName(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value) || value is "." or ".." ||
            value.IndexOfAny(['/', '\\']) >= 0 || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException($"'{value}' must be a single folder name.", paramName);
        }
    }

    private static List<GoldenFile> Normalise(IReadOnlyList<FileSaveContext> files)
    {
        var names = files.Select(f => NormaliseDate(f.FileName)).ToList();
        var result = new List<GoldenFile> { new(NamesFile, Encoding.UTF8.GetBytes(string.Join("\n", names) + "\n")) };
        foreach (var (file, name) in files.Zip(names))
        {
            if (file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            {
                using var zip = new ZipArchive(new MemoryStream(file.Content), ZipArchiveMode.Read);
                var worksheets = zip.Entries.Where(e =>
                    e.FullName.StartsWith("xl/worksheets/", StringComparison.Ordinal) &&
                    e.FullName.EndsWith(".xml", StringComparison.Ordinal));
                foreach (var entry in worksheets)
                {
                    using var content = new MemoryStream();
                    using (var entryStream = entry.Open())
                    {
                        entryStream.CopyTo(content);
                    }

                    result.Add(new GoldenFile($"{name}/{entry.FullName}", content.ToArray()));
                }
            }
            else
            {
                result.Add(new GoldenFile(name, NormaliseHeaderDate(file.Content)));
            }
        }

        return result;
    }

    private static string NormaliseDate(string text) => GenerationDate.Replace(text, DateToken);

    // Only the first line (the header) is normalised. Latin-1 maps every byte to one char and back,
    // so only the ASCII date bytes change: UTF-8 Cyrillic, the ╪ separator and line endings stay as they are.
    private static byte[] NormaliseHeaderDate(byte[] content)
    {
        var headerLength = Array.IndexOf(content, (byte)'\n') + 1;
        if (headerLength == 0)
        {
            headerLength = content.Length;
        }

        var header = Encoding.Latin1.GetBytes(NormaliseDate(Encoding.Latin1.GetString(content, 0, headerLength)));
        return [.. header, .. content.AsSpan(headerLength)];
    }

    private static string FirstDifference(byte[] expectedBytes, byte[] actualBytes)
    {
        var expected = Encoding.UTF8.GetString(expectedBytes);
        var actual = Encoding.UTF8.GetString(actualBytes);
        if (expected == actual)
        {
            // Different bytes that decode to the same text (e.g. invalid UTF-8).
            var offset = expectedBytes.AsSpan().CommonPrefixLength(actualBytes);
            return $"first difference at byte {offset} (the files differ only in bytes that are not valid UTF-8)";
        }

        var expectedLines = expected.Split('\n');
        var actualLines = actual.Split('\n');
        var line = 0;
        while (line < expectedLines.Length && line < actualLines.Length && expectedLines[line] == actualLines[line])
        {
            line++;
        }

        var expectedLine = line < expectedLines.Length ? expectedLines[line] : "<end of file>";
        var actualLine = line < actualLines.Length ? actualLines[line] : "<end of file>";
        var column = expectedLine.AsSpan().CommonPrefixLength(actualLine);

        return $"first difference at line {line + 1}, column {column + 1}" +
               $"{Environment.NewLine}  expected: {Excerpt(expectedLine, column)}" +
               $"{Environment.NewLine}  actual:   {Excerpt(actualLine, column)}";
    }

    // Worksheet XML can have very long lines, so show only the part around the first difference.
    // A trailing CR is shown as \r, so a line-ending-only difference is visible.
    private static string Excerpt(string line, int column)
    {
        const int before = 60, length = 160;
        var start = line.Length <= length ? 0 : Math.Max(0, column - before);
        var end = Math.Min(line.Length, start + length);
        var excerpt = line[start..end].Replace("\r", @"\r");
        return (start > 0 ? "…" : "") + excerpt + (end < line.Length ? "…" : "");
    }

    private sealed record GoldenFile(string RelativePath, byte[] Content);

    private sealed class GoldenSet(string fixture, string manufacturer, string directory)
    {
        public void Record(IEnumerable<GoldenFile> files)
        {
            // Start clean, so a file or worksheet that is no longer generated leaves no stale golden file.
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }

            foreach (var file in files)
            {
                var path = PathOf(file);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, file.Content);
            }
        }

        public void Compare(IReadOnlyList<GoldenFile> files)
        {
            foreach (var file in files)
            {
                var path = PathOf(file);
                if (!File.Exists(path))
                {
                    throw Mismatch(file.RelativePath, $"no golden file at {path}");
                }

                var expected = File.ReadAllBytes(path);
                if (!expected.AsSpan().SequenceEqual(file.Content))
                {
                    throw Mismatch(file.RelativePath, FirstDifference(expected, file.Content));
                }
            }

            // names.txt catches a missing or extra order file; this catches a worksheet an .xlsx no longer has.
            var generated = files.Select(f => f.RelativePath).ToHashSet();
            foreach (var xlsx in generated.Where(p => p.Contains('/')).Select(p => p[..p.IndexOf('/')]).Distinct())
            {
                var xlsxDirectory = Path.Combine(directory, xlsx);
                foreach (var golden in Directory.EnumerateFiles(xlsxDirectory, "*", SearchOption.AllDirectories))
                {
                    var relativePath = $"{xlsx}/{Path.GetRelativePath(xlsxDirectory, golden).Replace('\\', '/')}";
                    if (!generated.Contains(relativePath))
                    {
                        throw Mismatch(relativePath,
                            "the golden file exists, but this worksheet was not generated. If you just re-recorded, " +
                            "this may be a stale copy in the test output folder: rebuild clean.");
                    }
                }
            }
        }

        private string PathOf(GoldenFile file) =>
            Path.Combine([directory, .. file.RelativePath.Split('/')]);

        private GoldenFileMismatchException Mismatch(string file, string detail) =>
            new($"Golden mismatch for fixture '{fixture}', manufacturer '{manufacturer}', file '{file}': {detail}" +
                $"{Environment.NewLine}If the change is intended, re-record with UPDATE_GOLDEN=1 and explain the golden diff in the PR.");
    }
}

/// <summary>Thrown when generated order files do not match their golden files.</summary>
public sealed class GoldenFileMismatchException(string message) : Exception(message);
