using System.Text.Json;
using Jint;
using Jint.Native;

namespace Kroiko.Client.Tests.Hosting;

/// <summary>
/// The committed CloudFront Function, <c>hosting/cloudfront/viewer-request.js</c> (docs/implementation/07-hosting-and-go-live.md,
/// step 07a.1), run in Jint with the viewer-request event CloudFront passes it
/// (<see href="https://docs.aws.amazon.com/AmazonCloudFront/latest/DeveloperGuide/functions-event-structure.html"/>).
/// The unit tests and <see cref="E2E.StaticSiteHost"/> both call it, so the E2E tests go through the real function.
/// </summary>
internal sealed class ViewerRequestFunction
{
    /// <summary>CloudFront's limit on a function's code size.</summary>
    public const int MaxCodeBytes = 10 * 1024;

    private static readonly Lazy<ViewerRequestFunction> Committed = new(() => new ViewerRequestFunction(File.ReadAllText(SourcePath)));

    private readonly Engine _engine;
    private readonly Lock _lock = new(); // a Jint engine is not thread-safe, and Kestrel serves requests in parallel

    private ViewerRequestFunction(string source)
    {
        _engine = new Engine(options => options.Strict());
        _engine.Execute(source);
    }

    public static string SourcePath => Path.Combine(RepoPaths.Root, "hosting", "cloudfront", "viewer-request.js");

    /// <summary>The function as committed, loaded once.</summary>
    public static ViewerRequestFunction Instance => Committed.Value;

    /// <summary>
    /// The request the function returns for <paramref name="uri"/> (starting with <c>/</c>) and the viewer's
    /// <c>Accept-Encoding</c> (<see langword="null"/>: no such header).
    /// </summary>
    public ViewerRequest Run(string uri, string? acceptEncoding, string method = "GET")
    {
        var headers = new Dictionary<string, object> { ["host"] = new { value = "app.kroiko.com" } };
        if (acceptEncoding is not null)
            headers["accept-encoding"] = new { value = acceptEncoding };

        var cloudFrontEvent = JsonSerializer.Serialize(new
        {
            version = "1.0",
            context = new { distributionDomainName = "d111111abcdef8.cloudfront.net", eventType = "viewer-request" },
            viewer = new { ip = "127.0.0.1" },
            request = new { method, uri, querystring = new { }, headers, cookies = new { } },
        });

        string result;
        lock (_lock)
        {
            var returned = _engine.Evaluate($"JSON.stringify(handler({cloudFrontEvent}))");
            result = returned is JsString json ? json.ToString() : throw new InvalidOperationException("handler returned nothing");
        }

        using var document = JsonDocument.Parse(result);
        var root = document.RootElement;
        if (root.TryGetProperty("statusCode", out _))
            throw new InvalidOperationException($"handler returned a response, not a request: {result}");

        return new ViewerRequest(
            root.GetProperty("method").GetString()!,
            root.GetProperty("uri").GetString()!,
            root.GetProperty("headers").EnumerateObject()
                .ToDictionary(header => header.Name, header => header.Value.GetProperty("value").GetString()!));
    }
}

/// <summary>The request a viewer-request function hands on to the cache and the origin.</summary>
internal sealed record ViewerRequest(string Method, string Uri, IReadOnlyDictionary<string, string> Headers);
