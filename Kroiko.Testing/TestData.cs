namespace Kroiko.Testing;

/// <summary>
/// Paths to the shared test data under <c>TestData/</c>, which is copied to the output of every
/// test project that references <c>Kroiko.Testing</c> (ADR-0007 §2).
/// </summary>
public static class TestData
{
    private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "TestData");

    /// <summary>
    /// The full path of the synthetic Polyboard fixture <paramref name="fixture"/>: its file name
    /// in <c>TestData/polyboard/</c> without <c>.txt</c>, e.g. <c>"cabinet-23-field"</c>.
    /// </summary>
    public static string Polyboard(string fixture) =>
        Path.Combine(Root, "polyboard", fixture + ".txt");

    /// <summary>The output copy of the golden files, which <see cref="OrderFilesAssert"/> compares with.</summary>
    internal static string GoldenRoot => Path.Combine(Root, "golden");

    /// <summary>
    /// <c>Kroiko.Testing/TestData/golden/</c> in the source tree, which <c>UPDATE_GOLDEN=1</c> writes to.
    /// Found by walking up from the test assembly's folder to the folder that holds <c>Kroiko.Testing</c>.
    /// </summary>
    internal static string GoldenSourceRoot
    {
        get
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            {
                var project = Path.Combine(dir.FullName, "Kroiko.Testing");
                if (File.Exists(Path.Combine(project, "Kroiko.Testing.csproj")))
                {
                    return Path.Combine(project, "TestData", "golden");
                }
            }

            throw new DirectoryNotFoundException(
                $"UPDATE_GOLDEN=1: no Kroiko.Testing/Kroiko.Testing.csproj above {AppContext.BaseDirectory}; " +
                "golden files can only be recorded from a source checkout.");
        }
    }
}
