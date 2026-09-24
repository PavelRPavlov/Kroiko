using System.Text.Json;
using System.Text.RegularExpressions;

namespace Kroiko.Client.Tests.Hosting;

/// <summary>
/// What the published service worker precaches: the assets in <c>service-worker-assets.js</c> that match
/// <c>offlineAssetsInclude</c> and none of <c>offlineAssetsExclude</c> in <c>service-worker.js</c>, the same filter
/// its <c>onInstall</c> applies.
/// </summary>
internal static partial class PublishedServiceWorker
{
    /// <summary>The URLs, relative to the site root (e.g. <c>_framework/dotnet.native.abc.wasm</c>), the install caches.</summary>
    public static IReadOnlyList<string> PrecachedUrls(string webRoot)
    {
        var worker = File.ReadAllText(Path.Combine(webRoot, "service-worker.js"));
        var include = RegexList(worker, "offlineAssetsInclude");
        var exclude = RegexList(worker, "offlineAssetsExclude");

        return ManifestUrls(webRoot)
            .Where(url => include.Any(pattern => pattern.IsMatch(url)) && !exclude.Any(pattern => pattern.IsMatch(url)))
            .ToList();
    }

    /// <summary>Every asset URL in <c>service-worker-assets.js</c> (<c>self.assetsManifest = {…};</c>).</summary>
    public static IReadOnlyList<string> ManifestUrls(string webRoot)
    {
        var script = File.ReadAllText(Path.Combine(webRoot, "service-worker-assets.js"));
        var json = script[script.IndexOf('{')..(script.LastIndexOf('}') + 1)];
        using var manifest = JsonDocument.Parse(json);
        return manifest.RootElement.GetProperty("assets").EnumerateArray()
            .Select(asset => asset.GetProperty("url").GetString()!)
            .ToList();
    }

    // `const name = [ /a/, /b/i ];`: the JavaScript regex literals the template uses are also valid .NET patterns.
    // Anything in the list that is not such a literal fails loudly instead of being skipped.
    private static List<Regex> RegexList(string worker, string name)
    {
        var declaration = Regex.Match(worker, $@"const\s+{name}\s*=\s*\[(?<items>.*?)\]\s*;");
        if (!declaration.Success)
            throw new InvalidOperationException($"No `const {name} = [ … ];` in the published service-worker.js.");

        var items = declaration.Groups["items"].Value;
        var literals = RegexLiteral().Matches(items);
        if (RegexLiteral().Replace(items, "").Trim(',', ' ', '\t', '\r', '\n').Length > 0)
            throw new InvalidOperationException($"`{name}` holds something other than regex literals: [{items}]");

        return literals
            .Select(literal => new Regex(
                literal.Groups["pattern"].Value,
                literal.Groups["flags"].Value.Contains('i') ? RegexOptions.IgnoreCase : RegexOptions.None))
            .ToList();
    }

    // A /pattern/flags literal. A character class ([...]) may contain an unescaped '/', as JavaScript allows.
    [GeneratedRegex(@"/(?<pattern>(?:\\.|\[(?:\\.|[^\]\\])*\]|[^/\\\[\r\n])+)/(?<flags>[dgimsuvy]*)")]
    private static partial Regex RegexLiteral();
}
