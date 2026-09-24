using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;

namespace Kroiko.Client.Tests.E2E;

/// <summary>
/// Serves a published <c>wwwroot</c> the way a static host (Azure Static Web Apps) would, from an in-process
/// Kestrel on a free loopback port (ADR-0007 §5). <c>http://127.0.0.1</c> is a secure context, so the service
/// worker registers without a certificate. The files are served byte for byte (no rewriting): precompressed
/// <c>.br</c>/<c>.gz</c> siblings are negotiated by <c>Accept-Encoding</c>, app routes without a file
/// extension fall back to <c>index.html</c>, and <c>staticwebapp.config.json</c> is a 404, as on SWA.
/// </summary>
internal sealed class StaticSiteHost : IAsyncDisposable
{
    private const string ConfigFilePath = "/staticwebapp.config.json";

    // Preferred first, as a static host would.
    private static readonly (string Encoding, string Extension)[] Precompressed = [("br", ".br"), ("gzip", ".gz")];

    private readonly WebApplication _app;

    private StaticSiteHost(WebApplication app, Uri baseAddress)
    {
        _app = app;
        BaseAddress = baseAddress;
    }

    /// <summary>The site's root, ending in <c>/</c>.</summary>
    public Uri BaseAddress { get; }

    public static async Task<StaticSiteHost> StartAsync(string webRoot)
    {
        // Empty builder: no configuration sources (the site's own appsettings.json is content, not host
        // configuration), no logging providers, not Development (no static web assets manifest lookup).
        var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions
        {
            ContentRootPath = webRoot,
            WebRootPath = webRoot,
            EnvironmentName = "Production",
        });
        // Loopback, not "localhost": Kestrel cannot bind a dynamic port on "localhost". Both are secure contexts.
        builder.WebHost.UseKestrelCore().ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0));
        builder.Services.AddRouting();

        var app = builder.Build();
        var files = app.Environment.WebRootFileProvider;
        var contentTypes = new PrecompressedContentTypeProvider();

        app.Use((context, next) =>
        {
            // SWA reads its config file but never serves it (Azure/static-web-apps#259, #490).
            if (context.Request.Path.Equals(ConfigFilePath, StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return Task.CompletedTask;
            }

            NegotiatePrecompressed(context, files, contentTypes);
            return next(context);
        });
        app.UseStaticFiles(new StaticFileOptions { FileProvider = files, ContentTypeProvider = contentTypes });
        app.UseRouting();
        // The route pattern skips paths with a file extension, so a missing asset stays a 404.
        app.MapFallbackToFile("index.html", new StaticFileOptions { FileProvider = files });

        await app.StartAsync();

        var address = app.Services.GetRequiredService<IServer>().Features.GetRequiredFeature<IServerAddressesFeature>()
            .Addresses.Single();
        return new StaticSiteHost(app, new Uri(address.TrimEnd('/') + "/"));
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private static void NegotiatePrecompressed(HttpContext context, IFileProvider files, IContentTypeProvider contentTypes)
    {
        context.Response.Headers.Vary = "Accept-Encoding";

        // Only files the static-file middleware will serve: an unknown type is a 404, never a "br" 404.
        var path = context.Request.Path.Value;
        if (path is null
            || !(HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method))
            || !contentTypes.TryGetContentType(path, out _))
            return;

        var accepted = AcceptedEncodings(context.Request.Headers.AcceptEncoding.ToString());
        foreach (var (encoding, extension) in Precompressed)
        {
            if (!accepted.Contains(encoding) || !files.GetFileInfo(path + extension).Exists)
                continue;

            // The static-file middleware serves the sibling; its content type comes from the original name.
            context.Request.Path = path + extension;
            context.Response.Headers.ContentEncoding = encoding;
            return;
        }
    }

    private static HashSet<string> AcceptedEncodings(string header) =>
        header.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => StringWithQualityHeaderValue.TryParse(value, out var parsed) ? parsed : null)
            .Where(value => value is not null && value.Quality is not 0)
            .Select(value => value!.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The media types a published Blazor WebAssembly app needs, looked up by the original name when the
    /// request was rewritten to a <c>.br</c>/<c>.gz</c> sibling.
    /// </summary>
    private sealed class PrecompressedContentTypeProvider : IContentTypeProvider
    {
        private readonly FileExtensionContentTypeProvider _types = new()
        {
            Mappings =
            {
                [".wasm"] = "application/wasm",
                [".dat"] = "application/octet-stream",
                [".blat"] = "application/octet-stream",
                [".webmanifest"] = "application/manifest+json",
                [".woff2"] = "font/woff2",
                [".js"] = "text/javascript",
                [".map"] = "application/json",
            },
        };

        public bool TryGetContentType(string subpath, [MaybeNullWhen(false)] out string contentType)
        {
            foreach (var (_, extension) in Precompressed)
            {
                if (subpath.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                    return _types.TryGetContentType(subpath[..^extension.Length], out contentType);
            }

            return _types.TryGetContentType(subpath, out contentType);
        }
    }
}
