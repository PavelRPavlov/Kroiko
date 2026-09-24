using FluentAssertions;
using Xunit;

namespace Kroiko.Client.Tests.PublishScript;

/// <summary>
/// What the deploy puts in the release folder and how it switches to it (ADR-0010; docs/implementation/07-hosting-and-go-live.md
/// step 07a.2): a <c>raw/</c> and a <c>br/</c> tree with their metadata, the function kept at the committed code, and
/// a switch that changes only the origin path.
/// </summary>
public sealed class PublishScriptDeployTests(PublishScriptTemplate template) : PublishScriptTestBase(template)
{
    private const string Wasm = "_framework/dotnet.native.abcdefgh12.wasm";
    private const string Dat = "_framework/icudt_EFIGS.tptq2av103.dat";

    [Fact]
    public void Uploads_the_plain_files_to_raw_and_the_published_br_files_to_br()
    {
        var run = Repo.Run("main");

        run.ExitCode.Should().Be(0, run.Output);
        // The stub's files hold their own names; a .br sibling holds "<name>-br". No .br or .gz object is uploaded.
        ByTreePath(run, u => $"{u.Content}|{u.ContentEncoding}").Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["raw/index.html"] = "index|",
            ["br/index.html"] = "index-br|br",
            ["raw/service-worker.js"] = "worker|",
            ["br/service-worker.js"] = "worker-br|br",
            ["raw/service-worker-assets.js"] = "assets|",
            ["br/service-worker-assets.js"] = "assets|",
            [$"raw/{Wasm}"] = "wasm|",
            [$"br/{Wasm}"] = "wasm-br|br",
            [$"raw/{Dat}"] = "dat|",
            [$"br/{Dat}"] = "dat-br|br",
            ["raw/fonts/roboto.woff2"] = "font|",
            ["br/fonts/roboto.woff2"] = "font|",
            ["raw/manifest.webmanifest"] = "manifest|",
            ["br/manifest.webmanifest"] = "manifest-br|br",
            ["raw/css/app.css"] = "css|",
            ["br/css/app.css"] = "css|",
        });
    }

    [Fact]
    public void Gives_every_object_the_content_type_of_its_plain_name()
    {
        var run = Repo.Run("main");

        run.ExitCode.Should().Be(0, run.Output);
        ByPlainPath(run, u => u.ContentType).Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["index.html"] = "text/html; charset=utf-8",
            ["service-worker.js"] = "text/javascript; charset=utf-8",
            ["service-worker-assets.js"] = "text/javascript; charset=utf-8",
            [Wasm] = "application/wasm",
            [Dat] = "application/octet-stream",
            ["fonts/roboto.woff2"] = "font/woff2",
            ["manifest.webmanifest"] = "application/manifest+json",
            ["css/app.css"] = "text/css; charset=utf-8",
        });
    }

    [Fact]
    public void Revalidates_everything_except_the_fingerprinted_framework_files()
    {
        // ADR-0002: the update check must always see the current index.html and service-worker files.
        var run = Repo.Run("main");

        run.ExitCode.Should().Be(0, run.Output);
        const string immutable = "public, max-age=31536000, immutable";
        ByPlainPath(run, u => u.CacheControl).Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["index.html"] = "no-cache",
            ["service-worker.js"] = "no-cache",
            ["service-worker-assets.js"] = "no-cache",
            [Wasm] = immutable,
            [Dat] = immutable,
            ["fonts/roboto.woff2"] = "no-cache",
            ["manifest.webmanifest"] = "no-cache",
            ["css/app.css"] = "no-cache",
        });
    }

    [Theory]
    [InlineData("notes.xyz")]
    [InlineData("LICENSE")]
    public void Refuses_a_file_it_has_no_content_type_for_before_uploading_anything(string file)
    {
        var run = Repo.Run("main", new RunOptions { PublishExtraFile = file });

        run.ExitCode.Should().NotBe(0);
        run.Output.Should().Contain($"no Content-Type for {file}");
        run.AwsCommands.Should().Equal(["sts get-caller-identity"]);
    }

    [Fact]
    public void Changes_nothing_in_the_distribution_but_the_origin_path()
    {
        var run = Repo.Run("main");

        run.ExitCode.Should().Be(0, run.Output);
        run.Calls.Should().Contain("aws-config-otherwise-unchanged True");
        run.AwsCalls.Should().Contain(c => c.StartsWith("aws cloudfront update-distribution ") && c.Contains(" --if-match ETAG-DIST-1 "));
    }

    [Fact]
    public void Leaves_a_function_that_runs_the_committed_code_alone()
    {
        var run = Repo.Run("main");

        run.ExitCode.Should().Be(0, run.Output);
        run.AwsCommands.Should().NotContain(["cloudfront describe-function", "cloudfront update-function", "cloudfront publish-function"]);
    }

    [Fact]
    public void Brings_a_stale_function_up_to_the_committed_code_before_the_switch()
    {
        var run = Repo.Run("main", new RunOptions { LiveFunctionDiffers = true });

        run.ExitCode.Should().Be(0, run.Output);
        run.Calls.Should().Contain("aws-function-code-is-committed True");
        var commands = run.AwsCommands.ToList();
        commands.IndexOf("cloudfront update-function").Should().BeGreaterThan(commands.IndexOf("cloudfront describe-function"));
        commands.IndexOf("cloudfront publish-function").Should().BeGreaterThan(commands.IndexOf("cloudfront update-function"))
            .And.BeLessThan(commands.IndexOf("cloudfront update-distribution"));
        run.AwsCalls.Should().Contain(c => c.StartsWith("aws cloudfront update-function --name kroiko-pwa-main --if-match ETAG-DEV-1 "));
        run.AwsCalls.Should().Contain(c => c.StartsWith("aws cloudfront publish-function --name kroiko-pwa-main --if-match ETAG-DEV-2 "));
    }

    [Fact]
    public void Refuses_a_distribution_that_does_not_run_the_environments_function()
    {
        var run = Repo.Run("main", new RunOptions { DistributionFunction = "someone-elses-function" });

        run.ExitCode.Should().NotBe(0);
        run.Output.Should().Contain($"the distribution {PublishScriptSandbox.MainDistribution} does not run the function kroiko-pwa-main");
        run.AwsCommands.Should().NotContain("cloudfront update-distribution");
    }

    [Fact]
    public void Refuses_a_distribution_config_with_text_beyond_ascii()
    {
        // It comes back through the console, whose encoding on Windows follows the locale; it could be sent back garbled.
        var run = Repo.Run("main", new RunOptions { DistributionComment = "Кройко" });

        run.ExitCode.Should().NotBe(0);
        run.Output.Should().Contain("holds text beyond ASCII");
        run.AwsCommands.Should().NotContain("cloudfront update-distribution");
    }

    [Fact]
    public void Leaves_the_live_release_in_place_when_an_upload_fails()
    {
        var run = Repo.Run("main", new RunOptions { AwsFailsOn = "s3 cp" });

        run.ExitCode.Should().NotBe(0);
        run.Output.Should().Contain("uploading to s3://").And.Contain("the live release is unchanged");
        run.AwsCommands.Should().NotContain(c => c.StartsWith("cloudfront ", StringComparison.Ordinal));
        Directory.Exists(Path.GetDirectoryName(run.WebRoot)).Should().BeFalse("the temporary publish folder is removed");
    }

    [Fact]
    public void Says_how_to_finish_when_the_invalidation_fails_after_the_switch()
    {
        var run = Repo.Run("main", new RunOptions { AwsFailsOn = "create-invalidation" });

        run.ExitCode.Should().NotBe(0);
        run.Output.Should().Contain($"The distribution already serves {run.OriginPath}")
            .And.Contain($"aws cloudfront create-invalidation --distribution-id {PublishScriptSandbox.MainDistribution} --paths '/*' --profile kroiko-pwa");
    }

    /// <summary>The uploads by their path in the release folder (<c>raw/…</c>, <c>br/…</c>).</summary>
    private static Dictionary<string, string> ByTreePath(ScriptRun run, Func<Upload, string> value)
    {
        var releaseFolder = run.OriginPath!.TrimStart('/') + "/";
        run.Uploads.Should().OnlyContain(u => u.Key.StartsWith(releaseFolder));
        return run.Uploads.ToDictionary(u => u.Key[releaseFolder.Length..], value);
    }

    /// <summary>One value per plain path, after checking that the raw/ and br/ objects of each path agree on it.</summary>
    private static Dictionary<string, string> ByPlainPath(ScriptRun run, Func<Upload, string> value)
    {
        var byTreePath = ByTreePath(run, value);
        var raw = byTreePath.Where(p => p.Key.StartsWith("raw/")).ToDictionary(p => p.Key["raw/".Length..], p => p.Value);
        var br = byTreePath.Where(p => p.Key.StartsWith("br/")).ToDictionary(p => p.Key["br/".Length..], p => p.Value);
        br.Should().BeEquivalentTo(raw, "both trees serve the same file under the same headers");
        return raw;
    }
}
