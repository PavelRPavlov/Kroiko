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

    /// <summary>What every generation date is replaced with in the golden files.</summary>
    public const string DateToken = "{date}";

    // The one place that knows the generation-date format: the file-name providers write
    // DateTime.Now as yyyy-MM-dd (MegaTrading, Suliver). The .cut_mt carries no date today; it is
    // normalised the same way so a date added to its header would not make the golden files flaky.
    private static readonly Regex GenerationDate = new(@"(?<!\d)\d{4}-\d{2}-\d{2}(?!\d)", RegexOptions.CultureInvariant);

    /// <summary>
    /// Asserts that <paramref name="files"/>, in generation order, match the golden files recorded for
    /// <paramref name="fixture"/> and <paramref name="manufacturer"/>: the worksheet XML of every
    /// <c>.xlsx</c>, the bytes of every other file (a <c>.cut_mt</c>) and the file names in
    /// <c>names.txt</c>, with generation dates replaced by <see cref="DateToken"/>.
    /// With the environment variable <c>UPDATE_GOLDEN=1</c> it instead records <paramref name="files"/>
    /// as the golden files, in the source tree, and passes.
    /// </summary>
    /// <exception cref="GoldenFileMismatchException">The files do not match.</exception>
    public static void MatchGolden(string fixture, string manufacturer, IReadOnlyList<FileSaveContext> files)
    {
        var update = Environment.GetEnvironmentVariable("UPDATE_GOLDEN") == "1";
        MatchGolden(fixture, manufacturer, files, update ? TestData.GoldenSourceRoot : TestData.GoldenRoot, update);
    }

    internal static void MatchGolden(
        string fixture, string manufacturer, IReadOnlyList<FileSaveContext> files, string goldenRoot, bool update)
    {
        var expected = new GoldenSet(fixture, manufacturer, Path.Combine(goldenRoot, fixture, manufacturer));
        var actual = Normalise(files);

        if (update)
        {
            expected.Write(actual);
        }
        else
        {
            expected.Compare(actual);
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
                foreach (var entry in zip.Entries.Where(e => e.FullName.StartsWith("xl/worksheets/") && e.FullName.EndsWith(".xml")))
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
                result.Add(new GoldenFile(name, NormaliseDate(file.Content)));
            }
        }

        return result;
    }

    private static string NormaliseDate(string text) => GenerationDate.Replace(text, DateToken);

    // Latin-1 maps every byte to one char and back, so only the ASCII date bytes change: the
    // rest of the file (UTF-8 Cyrillic, the ╪ separator, line endings) stays byte for byte.
    private static byte[] NormaliseDate(byte[] content) =>
        Encoding.Latin1.GetBytes(NormaliseDate(Encoding.Latin1.GetString(content)));

    private static string FirstDifference(string expected, string actual)
    {
        var expectedLines = expected.Split('\n');
        var actualLines = actual.Split('\n');
        var line = 0;
        while (line < expectedLines.Length && line < actualLines.Length && expectedLines[line] == actualLines[line])
        {
            line++;
        }

        var expectedLine = line < expectedLines.Length ? expectedLines[line] : "<end of file>";
        var actualLine = line < actualLines.Length ? actualLines[line] : "<end of file>";
        var column = 0;
        while (column < expectedLine.Length && column < actualLine.Length && expectedLine[column] == actualLine[column])
        {
            column++;
        }

        return $"first difference at line {line + 1}, column {column + 1}" +
               $"{Environment.NewLine}  expected: {Excerpt(expectedLine, column)}" +
               $"{Environment.NewLine}  actual:   {Excerpt(actualLine, column)}";
    }

    // Worksheet XML is usually one long line, so show only the part around the first difference.
    private static string Excerpt(string line, int column)
    {
        const int before = 60, length = 160;
        line = line.TrimEnd('\r');
        if (line.Length <= length)
        {
            return line;
        }

        var start = Math.Max(0, column - before);
        var end = Math.Min(line.Length, start + length);
        return (start > 0 ? "…" : "") + line[start..end] + (end < line.Length ? "…" : "");
    }

    private sealed record GoldenFile(string RelativePath, byte[] Content);

    private sealed class GoldenSet(string fixture, string manufacturer, string directory)
    {
        public void Write(IEnumerable<GoldenFile> files)
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

        public void Compare(IEnumerable<GoldenFile> files)
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
                    throw Mismatch(file.RelativePath,
                        FirstDifference(Encoding.UTF8.GetString(expected), Encoding.UTF8.GetString(file.Content)));
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
                        throw Mismatch(relativePath, "the golden file exists, but this worksheet was not generated");
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
