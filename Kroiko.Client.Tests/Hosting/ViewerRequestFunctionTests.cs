using FluentAssertions;
using Xunit;

namespace Kroiko.Client.Tests.Hosting;

/// <summary>
/// The committed CloudFront Function does what docs/implementation/07-hosting-and-go-live.md step 07a.1 asks: an
/// <c>index.html</c> fallback that leaves missing assets a 404, the <c>br/</c> tree only for a viewer that accepts
/// <c>br</c>, and nothing else (ADR-0010). Runs the file itself in Jint; no browser, no publish.
/// </summary>
public sealed class ViewerRequestFunctionTests
{
    private static readonly ViewerRequestFunction Function = ViewerRequestFunction.Instance;

    [Theory]
    [InlineData("/")]
    [InlineData("/configuration")]
    [InlineData("/configuration/")]
    [InlineData("/some/deep/route")]
    public void An_app_route_gets_index_html(string path)
    {
        Function.Run(path, "br").Uri.Should().Be("/br/index.html");
        Function.Run(path, null).Uri.Should().Be("/raw/index.html");
    }

    [Theory]
    [InlineData("/index.html")]
    [InlineData("/_framework/dotnet.native.rw4kynp763.wasm")]
    [InlineData("/_framework/icudt_EFIGS.tptq2av103.dat")]
    [InlineData("/_framework/missing.js")]
    [InlineData("/_content/MudBlazor/missing.css")]
    [InlineData("/fonts/missing.woff2")]
    [InlineData("/manifest.webmanifest")]
    [InlineData("/service-worker.js")]
    public void A_file_keeps_its_path_so_a_missing_one_is_a_404(string path)
    {
        Function.Run(path, "br").Uri.Should().Be("/br" + path);
        Function.Run(path, "gzip").Uri.Should().Be("/raw" + path);
    }

    [Theory]
    [InlineData("br", true)]
    [InlineData("gzip, deflate, br", true)]
    [InlineData("gzip, deflate, br, zstd", true)]
    [InlineData("BR", true)]
    [InlineData(" br ;q=0.5", true)]
    [InlineData("br;q=1.0, gzip;q=0.8", true)]
    [InlineData("br;q=0, gzip", false)]
    [InlineData("br;q=0.0", false)]
    [InlineData("gzip", false)]
    [InlineData("gzip, deflate", false)]
    [InlineData("identity", false)]
    [InlineData("*", false)]
    [InlineData("brotli", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_a_viewer_that_accepts_br_gets_the_br_tree(string? acceptEncoding, bool brotli)
    {
        Function.Run("/app.js", acceptEncoding).Uri.Should().Be(brotli ? "/br/app.js" : "/raw/app.js");
    }

    [Theory]
    [InlineData("kroiko.com")]
    [InlineData("d111111abcdef8.cloudfront.net")] // staging
    public void Returns_the_request_with_only_its_uri_changed(string host)
    {
        // No generated response off www, no headers: S3's object metadata sets the response headers.
        var request = Function.Run("/configuration", "gzip, br", method: "HEAD", host: host);

        request.Method.Should().Be("HEAD");
        request.Headers.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["host"] = host,
            ["accept-encoding"] = "gzip, br",
        });
    }

    [Theory]
    [InlineData("/", "https://kroiko.com/")]
    [InlineData("/configuration", "https://kroiko.com/configuration")]
    [InlineData("/_framework/dotnet.native.rw4kynp763.wasm", "https://kroiko.com/_framework/dotnet.native.rw4kynp763.wasm")]
    public void Redirects_www_to_the_bare_domain_keeping_the_path(string path, string location)
    {
        // ADR-0011: one origin, so installs and saved settings never split between www and the bare domain.
        var response = Function.Respond(path, "www.kroiko.com");

        response.StatusCode.Should().Be(301);
        response.StatusDescription.Should().Be("Moved Permanently");
        response.Headers.Should().BeEquivalentTo(new Dictionary<string, string> { ["location"] = location });
    }

    [Fact]
    public void Redirects_www_whatever_the_case_of_the_host()
    {
        Function.Respond("/", "WWW.Kroiko.com").Headers["location"].Should().Be("https://kroiko.com/");
    }

    [Fact]
    public void Fits_cloudfronts_code_size_limit()
    {
        new FileInfo(ViewerRequestFunction.SourcePath).Length.Should().BeLessThan(ViewerRequestFunction.MaxCodeBytes);
    }
}
