using System.Diagnostics.CodeAnalysis;
using System.Net;
using Kroiko.Client.Tests.Hosting;
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
/// Serves a published <c>wwwroot</c> the way the deployed host does (ADR-0010), from an in-process Kestrel on a
/// free loopback port (ADR-0007 §5). <c>http://127.0.0.1</c> is a secure context, so the service worker registers
/// without a certificate. Every request goes through the committed CloudFront Function
/// (<see cref="ViewerRequestFunction"/>), which picks a release tree the way the deploy lays it out (step 07a.2):
/// <c>br/</c> serves a file's <c>.br</c> sibling with <c>Content-Encoding: br</c> where there is one, and <c>raw/</c>
/// the plain file. Files are served byte for byte. A missing file, a file of an unknown type and a <c>.br</c> or
/// <c>.gz</c> sibling asked for by name (the deploy uploads none) are 404s.
/// </summary>
internal sealed class StaticSiteHost : IAsyncDisposable
{
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

        var app = builder.Build();
        var files = app.Environment.WebRootFileProvider;
        var contentTypes = new PrecompressedContentTypeProvider();

        app.Use((context, next) =>
        {
            // The distributions allow GET and HEAD only; the static-file middleware ignores anything else.
            var readsAFile = HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method);
            if (readsAFile && !ServeFromReleaseTree(context, files, contentTypes))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return Task.CompletedTask;
            }

            return next(context);
        });
        // Whatever it does not find is the host's plain 404.
        app.UseStaticFiles(new StaticFileOptions { FileProvider = files, ContentTypeProvider = contentTypes });

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

    /// <summary>
    /// Runs the function and points the request at the file its tree holds. <see langword="false"/>: the tree holds
    /// no object under that name.
    /// </summary>
    private static bool ServeFromReleaseTree(HttpContext context, IFileProvider files, IContentTypeProvider contentTypes)
    {
        var acceptEncoding = context.Request.Headers.AcceptEncoding.ToString();
        var uri = ViewerRequestFunction.Instance
            .Run(context.Request.Path.Value ?? "/", acceptEncoding.Length > 0 ? acceptEncoding : null, context.Request.Method)
            .Uri;

        var (tree, path) = uri switch
        {
            _ when uri.StartsWith("/br/", StringComparison.Ordinal) => ("br", uri["/br".Length..]),
            _ when uri.StartsWith("/raw/", StringComparison.Ordinal) => ("raw", uri["/raw".Length..]),
            _ => throw new InvalidOperationException($"The function sent {context.Request.Path} to {uri}, outside both trees."),
        };

        if (PrecompressedContentTypeProvider.IsPrecompressedSibling(path))
            return false;

        context.Request.Path = path;
        if (tree == "br" && contentTypes.TryGetContentType(path, out _) && files.GetFileInfo(path + ".br").Exists)
        {
            // The static-file middleware serves the sibling; its content type comes from the original name.
            context.Request.Path = path + ".br";
            context.Response.Headers.ContentEncoding = "br";
        }

        return true;
    }

    /// <summary>
    /// The media types a published Blazor WebAssembly app needs, looked up by the original name when the
    /// request was pointed at a <c>.br</c> sibling.
    /// </summary>
    private sealed class PrecompressedContentTypeProvider : IContentTypeProvider
    {
        private static readonly string[] SiblingExtensions = [".br", ".gz"];

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

        public static bool IsPrecompressedSibling(string path) =>
            SiblingExtensions.Any(extension => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase));

        public bool TryGetContentType(string subpath, [MaybeNullWhen(false)] out string contentType)
        {
            foreach (var extension in SiblingExtensions)
            {
                if (subpath.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                    return _types.TryGetContentType(subpath[..^extension.Length], out contentType);
            }

            return _types.TryGetContentType(subpath, out contentType);
        }
    }
}
