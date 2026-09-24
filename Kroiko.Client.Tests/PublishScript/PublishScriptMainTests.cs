using FluentAssertions;
using Xunit;

namespace Kroiko.Client.Tests.PublishScript;

/// <summary><c>-Environment main</c>: staging deploys only <c>origin/main</c> itself (ADR-0001).</summary>
public sealed class PublishScriptMainTests(PublishScriptTemplate template) : PublishScriptTestBase(template)
{
    [Fact]
    public void Deploys_origin_main_to_the_main_environment_after_the_full_test_run()
    {
        var run = Repo.Run("main");

        run.ExitCode.Should().Be(0, run.Output);
        run.DotnetCalls.Should().HaveCount(2);
        run.DotnetCalls.First().Should().StartWith("dotnet test ").And.EndWith("TextConverter.sln", "the whole solution, E2E included");
        run.DotnetCalls.Last().Should().MatchRegex(@"^dotnet publish \S+Kroiko\.Client\.Blazor\.csproj -c Release -o \S+$");

        var publishDir = run.DotnetCalls.Last().Split(' ').Last();
        var webRoot = Path.Combine(publishDir, "wwwroot");
        run.SwaCalls.Should().Equal($"swa deploy {webRoot} --env main --swa-config-location {webRoot}");
        Directory.Exists(publishDir).Should().BeFalse("the temporary publish folder is removed");
    }

    [Fact]
    public void Prints_the_deployed_version_and_url()
    {
        Repo.SetVersion("0.1.0");
        Repo.Commit("version 0.1.0");
        Repo.Push("main");
        var shortSha = Repo.Git("rev-parse", "--short=7", "HEAD");

        var run = Repo.Run("main");

        run.ExitCode.Should().Be(0, run.Output);
        run.Output.Should().Contain($"Deployed v0.1.0 ({shortSha}) to main: {PublishScriptSandbox.StubUrl}");
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
