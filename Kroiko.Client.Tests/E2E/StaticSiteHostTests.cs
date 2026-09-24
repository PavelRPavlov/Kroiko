using System.Net;
using FluentAssertions;
using Xunit;

namespace Kroiko.Client.Tests.E2E;

/// <summary>
/// The static host the E2E tests serve the published app from. No browser: plain HTTP requests
/// against a tiny site in a temp folder.
/// </summary>
public sealed class StaticSiteHostTests : IAsyncLifetime
{
    private readonly string _webRoot = Directory.CreateTempSubdirectory("kroiko-static-host-").FullName;
    private StaticSiteHost _host = null!;
    private HttpClient _http = null!;

    public async Task InitializeAsync()
    {
        Write("index.html", "<html>index</html>");
        Write("app.js", "console.log('plain');");
        Write("app.js.br", "brotli-bytes");
        Write("app.js.gz", "gzip-bytes");
        Write("only-plain.css", "body{}");
        Write("fonts/roboto.woff2", "font");
        Write("_framework/dotnet.native.wasm", "wasm");
        Write("_framework/icudt_EFIGS.dat", "icu");
        Write("manifest.webmanifest", "{}");
        Write("notes.unknown", "?");
        Write("notes.unknown.br", "?");

        _host = await StaticSiteHost.StartAsync(_webRoot);
        // No automatic decompression: the tests look at the encoding the host chose.
        _http = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.None })
        {
            BaseAddress = _host.BaseAddress,
        };
    }

    public async Task DisposeAsync()
    {
        _http.Dispose();
        await _host.DisposeAsync();
        Directory.Delete(_webRoot, recursive: true);
    }

    [Fact]
    public void Listens_on_a_loopback_port()
    {
        _host.BaseAddress.IsLoopback.Should().BeTrue();
        _host.BaseAddress.Port.Should().BePositive();
        _host.BaseAddress.AbsolutePath.Should().Be("/");
    }

    [Theory]
    [InlineData("_framework/dotnet.native.wasm", "application/wasm")]
    [InlineData("_framework/icudt_EFIGS.dat", "application/octet-stream")]
    [InlineData("manifest.webmanifest", "application/manifest+json")]
    [InlineData("fonts/roboto.woff2", "font/woff2")]
    [InlineData("only-plain.css", "text/css")]
    public async Task Serves_the_blazor_file_types(string path, string mediaType)
    {
        using var response = await _http.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be(mediaType);
    }

    [Theory]
    [InlineData("br", "br", "brotli-bytes")]
    [InlineData("gzip", "gzip", "gzip-bytes")]
    [InlineData("gzip, deflate, br", "br", "brotli-bytes")]
    [InlineData("br;q=0, gzip", "gzip", "gzip-bytes")]
    [InlineData("identity", null, "console.log('plain');")]
    public async Task Negotiates_the_precompressed_file(string acceptEncoding, string? contentEncoding, string body)
    {
        using var response = await GetAsync("app.js", acceptEncoding);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be(body);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/javascript");
        response.Content.Headers.ContentEncoding.SingleOrDefault().Should().Be(contentEncoding);
        response.Headers.Vary.Should().Contain("Accept-Encoding");
    }

    [Fact]
    public async Task Serves_the_plain_file_when_there_is_no_precompressed_one()
    {
        using var response = await GetAsync("only-plain.css", "br, gzip");

        (await response.Content.ReadAsStringAsync()).Should().Be("body{}");
        response.Content.Headers.ContentEncoding.Should().BeEmpty();
    }

    [Theory]
    [InlineData("configuration")]
    [InlineData("some/deep/route")]
    public async Task Falls_back_to_index_html_for_app_routes(string route)
    {
        using var response = await _http.GetAsync(route);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
        (await response.Content.ReadAsStringAsync()).Should().Be("<html>index</html>");
    }

    [Fact]
    public async Task A_missing_file_is_a_404_not_index_html()
    {
        // An HTML fallback served for a .wasm would surface as an integrity failure, not a clear 404.
        using var response = await _http.GetAsync("_framework/missing.wasm");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_file_of_an_unknown_type_is_a_plain_404_even_with_a_precompressed_sibling()
    {
        using var response = await GetAsync("notes.unknown", "br");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentEncoding.Should().BeEmpty();
    }

    private async Task<HttpResponseMessage> GetAsync(string path, string acceptEncoding)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.TryAddWithoutValidation("Accept-Encoding", acceptEncoding);
        return await _http.SendAsync(request);
    }

    private void Write(string relativePath, string content)
    {
        var path = Path.Combine(_webRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content); // UTF-8, no BOM
    }
}
