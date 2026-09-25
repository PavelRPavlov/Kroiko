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

    /// <summary>The production origin (ADR-0011), the <c>Host</c> a request carries unless a test says otherwise.</summary>
    public const string ProductionHost = "kroiko.com";

    /// <summary>
    /// The request the function hands on for <paramref name="uri"/> (starting with <c>/</c>) and the viewer's
    /// <c>Accept-Encoding</c> (<see langword="null"/>: no such header). Fails if the function answers itself.
    /// </summary>
    public ViewerRequest Run(string uri, string? acceptEncoding, string method = "GET", string host = ProductionHost)
    {
        using var document = Evaluate(uri, acceptEncoding, method, host);
        var root = document.RootElement;
        if (root.TryGetProperty("statusCode", out _))
            throw new InvalidOperationException($"handler returned a response, not a request: {root}");

        return new ViewerRequest(root.GetProperty("method").GetString()!, root.GetProperty("uri").GetString()!, Headers(root));
    }

    /// <summary>The response the function answers with itself, such as a redirect. Fails if it hands the request on.</summary>
    public ViewerResponse Respond(string uri, string host, string? acceptEncoding = "br")
    {
        using var document = Evaluate(uri, acceptEncoding, "GET", host);
        var root = document.RootElement;
        if (!root.TryGetProperty("statusCode", out var statusCode))
            throw new InvalidOperationException($"handler returned a request, not a response: {root}");

        return new ViewerResponse(statusCode.GetInt32(), root.GetProperty("statusDescription").GetString()!, Headers(root));
    }

    private JsonDocument Evaluate(string uri, string? acceptEncoding, string method, string host)
    {
        var headers = new Dictionary<string, object> { ["host"] = new { value = host } };
        if (acceptEncoding is not null)
            headers["accept-encoding"] = new { value = acceptEncoding };

        var cloudFrontEvent = JsonSerializer.Serialize(new
        {
            version = "1.0",
            context = new { distributionDomainName = "d111111abcdef8.cloudfront.net", eventType = "viewer-request" },
            viewer = new { ip = "127.0.0.1" },
            request = new { method, uri, querystring = new { }, headers, cookies = new { } },
        });

        lock (_lock)
        {
            var returned = _engine.Evaluate($"JSON.stringify(handler({cloudFrontEvent}))");
            return JsonDocument.Parse(returned is JsString json ? json.ToString() : throw new InvalidOperationException("handler returned nothing"));
        }
    }

    private static Dictionary<string, string> Headers(JsonElement message) =>
        message.GetProperty("headers").EnumerateObject()
            .ToDictionary(header => header.Name, header => header.Value.GetProperty("value").GetString()!);
}

/// <summary>The request a viewer-request function hands on to the cache and the origin.</summary>
internal sealed record ViewerRequest(string Method, string Uri, IReadOnlyDictionary<string, string> Headers);

/// <summary>A response a viewer-request function sends the viewer itself, without the cache or the origin.</summary>
internal sealed record ViewerResponse(int StatusCode, string StatusDescription, IReadOnlyDictionary<string, string> Headers);
