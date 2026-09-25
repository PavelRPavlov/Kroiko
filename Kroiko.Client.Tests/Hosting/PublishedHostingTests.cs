using FluentAssertions;
using Kroiko.Client.Tests.E2E;
using Xunit;

namespace Kroiko.Client.Tests.Hosting;

/// <summary>
/// The Release publish and the CloudFront Function agree (docs/implementation/07-hosting-and-go-live.md, step 07a.1):
/// every published file is reachable at its own path, so the fallback never serves <c>index.html</c> in its place.
/// The tests only read files, but from the E2E fixture's publish output, so they run with the E2E tests.
/// </summary>
[Collection(E2ECollection.Name)]
[Trait("Category", "E2E")]
public sealed class PublishedHostingTests(PublishedApp app)
{
    [Fact]
    public void Every_precached_asset_keeps_its_path_through_the_function()
    {
        // A precached asset served as index.html fails the install with an integrity error that hides the cause.
        var precached = PublishedServiceWorker.PrecachedUrls(app.WebRoot);

        precached.Should().Contain(url => url.StartsWith("_framework/", StringComparison.Ordinal));
        precached.Should().Contain("index.html");
        precached.Should().AllSatisfy(url => ShouldKeepItsPath("/" + url));
    }

    [Fact]
    public void Every_published_file_keeps_its_path_through_the_function()
    {
        // The deploy uploads every file (step 07a.2), including those the service worker does not precache.
        var published = Directory.EnumerateFiles(app.WebRoot, "*", SearchOption.AllDirectories)
            .Where(file => !file.EndsWith(".br", StringComparison.Ordinal) && !file.EndsWith(".gz", StringComparison.Ordinal))
            .Select(file => "/" + Path.GetRelativePath(app.WebRoot, file).Replace('\\', '/'))
            .ToList();

        published.Should().Contain(["/index.html", "/service-worker.js", "/service-worker-assets.js"]);
        published.Should().AllSatisfy(ShouldKeepItsPath);
    }

    private static void ShouldKeepItsPath(string path)
    {
        ViewerRequestFunction.Instance.Run(path, "br").Uri.Should().Be("/br" + path);
        ViewerRequestFunction.Instance.Run(path, null).Uri.Should().Be("/raw" + path);
    }
}
