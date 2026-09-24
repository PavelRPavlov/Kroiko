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
}
