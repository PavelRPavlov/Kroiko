namespace Kroiko.Client.Tests;

/// <summary>Paths in the repository the tests read from, found by walking up to <c>TextConverter.sln</c>.</summary>
internal static class RepoPaths
{
    private static readonly Lazy<string> RootPath = new(FindRoot);

    public static string Root => RootPath.Value;

    public static string ClientProject => Path.Combine(Root, "Kroiko.Client.Blazor", "Kroiko.Client.Blazor.csproj");

    /// <summary>The client's source <c>wwwroot</c> (not a published one).</summary>
    public static string ClientWebRoot => Path.Combine(Root, "Kroiko.Client.Blazor", "wwwroot");

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "TextConverter.sln")))
                return dir.FullName;
        }

        throw new InvalidOperationException($"No TextConverter.sln above {AppContext.BaseDirectory}.");
    }
}
