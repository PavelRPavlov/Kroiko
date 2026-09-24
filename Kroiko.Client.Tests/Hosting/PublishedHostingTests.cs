using FluentAssertions;
using Kroiko.Client.Tests.E2E;
using Xunit;

namespace Kroiko.Client.Tests.Hosting;

/// <summary>
/// The Release publish carries <c>staticwebapp.config.json</c> where Azure Static Web Apps reads it, and the
/// config and the service worker agree (docs/implementation/07-hosting-and-go-live.md, step 07a.1). Reads the
/// E2E fixture's publish output; no browser.
/// </summary>
[Collection(E2ECollection.Name)]
[Trait("Category", "E2E")]
public sealed class PublishedHostingTests(PublishedApp app)
{
    [Fact]
    public void The_config_is_published_unchanged_at_the_site_root()
    {
        // `swa deploy <publish>/wwwroot` reads it from the root of the folder it deploys.
        var published = Path.Combine(app.WebRoot, StaticWebAppConfig.FileName);

        File.Exists(published).Should().BeTrue();
        File.ReadAllBytes(published).Should().Equal(File.ReadAllBytes(StaticWebAppConfig.SourcePath));
    }

    [Fact]
    public void The_service_worker_does_not_precache_the_config()
    {
        // SWA never serves its config file; precaching it would 404 and fail the whole install (Azure/static-web-apps#259, #490).
        PublishedServiceWorker.ManifestUrls(app.WebRoot).Should().Contain(StaticWebAppConfig.FileName, "it is published under wwwroot");
        PublishedServiceWorker.PrecachedUrls(app.WebRoot).Should().NotContain(StaticWebAppConfig.FileName);
    }

    [Fact]
    public void Every_precached_asset_is_excluded_from_the_navigation_fallback()
    {
        // A missing precached asset must be a 404: index.html in its place fails the install with an integrity error
        // that hides the cause. index.html itself is the fallback target.
        var config = StaticWebAppConfig.Load(Path.Combine(app.WebRoot, StaticWebAppConfig.FileName));

        var falling = PublishedServiceWorker.PrecachedUrls(app.WebRoot)
            .Where(url => url != "index.html")
            .Where(url => !config.IsExcludedFromNavigationFallback("/" + url));

        PublishedServiceWorker.PrecachedUrls(app.WebRoot).Should().Contain(url => url.StartsWith("_framework/"));
        falling.Should().BeEmpty();
    }
}
