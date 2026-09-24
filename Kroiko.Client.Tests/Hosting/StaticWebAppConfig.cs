using System.Text.Json;
using System.Text.RegularExpressions;

namespace Kroiko.Client.Tests.Hosting;

/// <summary>
/// The Azure Static Web Apps configuration, <c>staticwebapp.config.json</c> (docs/implementation/07-hosting-and-go-live.md,
/// step 07a.1), read the way the tests need it. Schema:
/// <see href="https://learn.microsoft.com/en-us/azure/static-web-apps/configuration"/>.
/// </summary>
internal sealed class StaticWebAppConfig
{
    public const string FileName = "staticwebapp.config.json";

    private readonly JsonElement _root;

    private StaticWebAppConfig(JsonElement root) => _root = root;

    /// <summary>The config as committed: <c>Kroiko.Client.Blazor/wwwroot/staticwebapp.config.json</c>.</summary>
    public static string SourcePath => Path.Combine(RepoPaths.ClientWebRoot, FileName);

    public static StaticWebAppConfig Load(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return new StaticWebAppConfig(document.RootElement.Clone());
    }

    /// <summary>The top-level sections, e.g. <c>navigationFallback</c>, <c>routes</c>, <c>mimeTypes</c>.</summary>
    public IReadOnlyList<string> Sections => _root.EnumerateObject().Select(property => property.Name).ToList();

    public string? FallbackRewrite =>
        NavigationFallback is { } fallback && fallback.TryGetProperty("rewrite", out var rewrite) ? rewrite.GetString() : null;

    public IReadOnlyList<string> FallbackExcludes =>
        NavigationFallback is { } fallback && fallback.TryGetProperty("exclude", out var exclude)
            ? exclude.EnumerateArray().Select(value => value.GetString()!).ToList()
            : [];

    /// <summary>Each route rule as its property names, e.g. <c>route</c>, <c>headers</c>.</summary>
    public IReadOnlyList<IReadOnlyList<string>> RouteRuleProperties =>
        Routes.Select(IReadOnlyList<string> (rule) => rule.EnumerateObject().Select(property => property.Name).ToList()).ToList();

    /// <summary>The <c>headers</c> of each route rule, by its <c>route</c>.</summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> RouteHeaders =>
        Routes.ToDictionary(
            rule => rule.GetProperty("route").GetString()!,
            IReadOnlyDictionary<string, string> (rule) => rule.TryGetProperty("headers", out var headers)
                ? StringMap(headers)
                : new Dictionary<string, string>());

    public IReadOnlyDictionary<string, string> MimeTypes =>
        _root.TryGetProperty("mimeTypes", out var mimeTypes) ? StringMap(mimeTypes) : new Dictionary<string, string>();

    /// <summary>
    /// Whether a request for <paramref name="urlPath"/> (starting with <c>/</c>) is excluded from the navigation
    /// fallback, so a missing file there is a 404 instead of <c>index.html</c>. A rule has at most one <c>*</c>:
    /// <c>/css/*</c> matches everything under <c>/css/</c>, and <c>*.{png,ico}</c> matches those extensions at any
    /// depth, as the SWA CLI emulator matches them
    /// (<see href="https://github.com/Azure/static-web-apps-cli/blob/main/src/core/utils/glob.ts">globToRegExp</see>).
    /// </summary>
    public bool IsExcludedFromNavigationFallback(string urlPath) =>
        FallbackExcludes.Any(rule => Regex.IsMatch(urlPath, GlobToRegex(rule)));

    private JsonElement? NavigationFallback =>
        _root.TryGetProperty("navigationFallback", out var fallback) ? fallback : null;

    private IEnumerable<JsonElement> Routes =>
        _root.TryGetProperty("routes", out var routes) ? routes.EnumerateArray() : [];

    private static Dictionary<string, string> StringMap(JsonElement map) =>
        map.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.GetString()!);

    private static string GlobToRegex(string glob)
    {
        var pattern = Regex.Escape(glob).Replace(@"\*", ".*");
        pattern = Regex.Replace(pattern, @"\\\{([^}]*)}", match => $"({match.Groups[1].Value.Replace(",", "|")})");
        return $"^{pattern}$";
    }
}
