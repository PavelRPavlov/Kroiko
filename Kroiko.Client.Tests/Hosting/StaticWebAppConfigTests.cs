using FluentAssertions;
using Xunit;

namespace Kroiko.Client.Tests.Hosting;

/// <summary>
/// The committed <c>staticwebapp.config.json</c> says what docs/implementation/07-hosting-and-go-live.md step 07a.1
/// asks: an <c>index.html</c> fallback that leaves missing assets a 404, <c>no-cache</c> on the files the update
/// check reads (ADR-0002), the Blazor MIME types, and nothing else (ADR-0001). No browser, no publish.
/// </summary>
public sealed class StaticWebAppConfigTests
{
    private readonly StaticWebAppConfig _config = StaticWebAppConfig.Load(StaticWebAppConfig.SourcePath);

    [Fact]
    public void Has_only_the_fallback_the_routes_and_the_mime_types()
    {
        // No auth, no API, no global headers or overrides.
        _config.Sections.Should().BeEquivalentTo("navigationFallback", "routes", "mimeTypes");
    }

    [Fact]
    public void Falls_back_to_index_html()
    {
        _config.FallbackRewrite.Should().Be("/index.html");
    }

    [Theory]
    [InlineData("/_framework/missing.wasm")]
    [InlineData("/_framework/dotnet.missing.js")]
    [InlineData("/_framework/icudt_missing.dat")]
    [InlineData("/_framework/anything")]
    [InlineData("/css/missing.css")]
    [InlineData("/js/missing.js")]
    [InlineData("/fonts/missing.woff2")]
    [InlineData("/_content/MudBlazor/missing.css")]
    [InlineData("/Assets/missing.png")]
    [InlineData("/missing.js")]
    [InlineData("/missing.json")]
    [InlineData("/missing.webmanifest")]
    [InlineData("/missing.ico")]
    public void A_missing_asset_is_excluded_from_the_fallback(string path)
    {
        _config.IsExcludedFromNavigationFallback(path).Should().BeTrue("a missing asset must be a 404, not index.html");
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/configuration")]
    [InlineData("/some/deep/route")]
    public void An_app_route_falls_back_to_index_html(string path)
    {
        _config.IsExcludedFromNavigationFallback(path).Should().BeFalse();
    }

    [Fact]
    public void Excludes_exactly_the_asset_folders_and_extensions_of_the_guide()
    {
        _config.FallbackExcludes.Should().BeEquivalentTo(
            "/_framework/*", "/css/*", "/js/*", "/fonts/*", "*.{js,css,wasm,dat,json,webmanifest,png,ico,woff2}");
    }

    [Fact]
    public void The_update_check_files_are_never_served_from_a_cache_without_revalidation()
    {
        // The route for /index.html also matches "/" (SWA serves index.html as the folder's default file).
        var noCache = new Dictionary<string, string> { ["Cache-Control"] = "no-cache" };

        _config.RouteHeaders.Should().BeEquivalentTo(new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            ["/index.html"] = noCache,
            ["/service-worker.js"] = noCache,
            ["/service-worker-assets.js"] = noCache,
        });
        _config.RouteRuleProperties.Should().AllSatisfy(properties =>
            properties.Should().BeEquivalentTo(["route", "headers"], "the routes only add headers: no rewrites, redirects or roles"));
    }

    [Fact]
    public void Serves_the_blazor_file_types_with_their_mime_types()
    {
        _config.MimeTypes.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            [".webmanifest"] = "application/manifest+json",
            [".dat"] = "application/octet-stream",
            [".wasm"] = "application/wasm",
            [".woff2"] = "font/woff2",
        });
    }
}
