using FluentAssertions;
using Xunit;

namespace Kroiko.Client.Tests.PublishScript;

/// <summary><c>-Environment main</c>: staging deploys only <c>origin/main</c> itself (ADR-0010).</summary>
public sealed class PublishScriptMainTests(PublishScriptTemplate template) : PublishScriptTestBase(template)
{
    [Fact]
    public void Deploys_origin_main_to_the_main_distribution_after_the_full_test_run()
    {
        var run = Repo.Run("main");

        run.ExitCode.Should().Be(0, run.Output);
        run.DotnetCalls.Should().HaveCount(2);
        run.DotnetCalls.First().Should().StartWith("dotnet test ").And.EndWith("TextConverter.sln", "the whole solution, E2E included");
        run.DotnetCalls.Last().Should().MatchRegex(@"^dotnet publish \S+Kroiko\.Client\.Blazor\.csproj -c Release -o \S+$");

        // Credentials first, then every upload, then the switch; the new release goes live only once it is complete.
        run.AwsCommands.First().Should().Be("sts get-caller-identity");
        run.AwsCommands.Skip(1).TakeWhile(c => c == "s3 cp").Should().NotBeEmpty();
        run.AwsCommands.Where(c => c is not ("sts get-caller-identity" or "s3 cp")).Should().Equal(
            "cloudfront get-function",
            "cloudfront get-distribution-config",
            "cloudfront get-distribution-config",
            "cloudfront update-distribution",
            "cloudfront wait",
            "cloudfront create-invalidation",
            "cloudfront wait");
        run.AwsCalls.Where(c => c.Contains(" --id ") || c.Contains(" --distribution-id "))
            .Should().OnlyContain(c => c.Contains(PublishScriptSandbox.MainDistribution));
        run.AwsCalls.Should().Contain(c => c.StartsWith("aws cloudfront get-function --name kroiko-pwa-main --stage LIVE "));
        run.AwsCalls.Should().Contain(c => c.StartsWith("aws cloudfront wait distribution-deployed --id "));
        run.AwsCalls.Should().Contain(c => c.StartsWith("aws cloudfront create-invalidation ") && c.Contains(" --paths /* "));
        run.AwsCalls.Should().Contain(c => c.StartsWith("aws cloudfront wait invalidation-completed ") && c.Contains(" --id I2SANDBOXINVALIDATION "));

        Directory.Exists(Path.GetDirectoryName(run.WebRoot)).Should().BeFalse("the temporary publish folder is removed");
    }

    [Fact]
    public void Uploads_a_new_release_folder_and_switches_the_origin_path_to_it()
    {
        var shortSha = Repo.Git("rev-parse", "--short=7", "HEAD");

        var run = Repo.Run("main");

        run.ExitCode.Should().Be(0, run.Output);
        run.OriginPath.Should().MatchRegex($@"^/main/\d{{8}}T\d{{6}}Z-v1\.0\.0-{shortSha}$");
        var releaseFolder = run.OriginPath!.TrimStart('/') + "/";
        run.Uploads.Should().OnlyContain(upload => upload.Key.StartsWith(releaseFolder), "every object lands in the new folder");
        run.OriginPath.Should().NotBe(PublishScriptSandbox.PreviousOriginPath);
    }

    [Fact]
    public void Prints_the_deployed_version_url_and_release_folder()
    {
        Repo.SetVersion("0.1.0");
        Repo.Commit("version 0.1.0");
        Repo.Push("main");
        var shortSha = Repo.Git("rev-parse", "--short=7", "HEAD");

        var run = Repo.Run("main");

        run.ExitCode.Should().Be(0, run.Output);
        run.Output.Should().Contain($"Deployed v0.1.0 ({shortSha}) to main: {PublishScriptSandbox.MainUrl}");
        run.Output.Should().Contain($"Release folder: s3://{PublishScriptSandbox.Bucket}{run.OriginPath}/");
        run.Output.Should().Contain($"from {PublishScriptSandbox.PreviousOriginPath} to {run.OriginPath}");
    }

    [Fact]
    public void Refuses_main_when_HEAD_has_commits_origin_main_does_not()
    {
        Repo.Commit("local only");

        var run = Repo.Run("main");

        run.ExitCode.Should().NotBe(0);
        run.Output.Should().Contain("HEAD is not origin/main");
        run.Calls.Should().BeEmpty();
    }

    [Fact]
    public void Refuses_main_when_origin_main_moved_on_since_the_last_fetch()
    {
        Repo.Commit("pushed by someone else");
        Repo.Push("main");
        Repo.Git("reset", "--quiet", "--hard", "HEAD~1");
        Repo.Git("rev-parse", "origin/main").Should().Be(Repo.Git("rev-parse", "HEAD"), "only a fetch shows that origin moved");

        var run = Repo.Run("main");

        run.ExitCode.Should().NotBe(0);
        run.Output.Should().Contain("HEAD is not origin/main");
        run.Calls.Should().BeEmpty();
    }
}
