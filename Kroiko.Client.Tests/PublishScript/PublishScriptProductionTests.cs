using FluentAssertions;
using Xunit;

namespace Kroiko.Client.Tests.PublishScript;

/// <summary>
/// <c>-Environment production</c>: only <c>origin/release</c>, checked out on <c>release</c>, tagged <c>vX.Y.Z</c> with
/// the csproj <c>&lt;Version&gt;</c> (ADR-0001, ADR-0002 §6).
/// </summary>
public sealed class PublishScriptProductionTests(PublishScriptTemplate template) : PublishScriptTestBase(template)
{
    [Fact]
    public void Deploys_a_tagged_release_to_production()
    {
        ReleaseVersion("1.2.3", tag: "v1.2.3");

        var run = Repo.Run("production");

        run.ExitCode.Should().Be(0, run.Output);
        run.DotnetCalls.First().Should().StartWith("dotnet test ");
        run.SwaCalls.Should().Equal(run.ExpectedSwaDeploy("production"));
        run.Output.Should().Contain("Deployed v1.2.3 (").And.Contain("https://app.kroiko.com");
    }

    [Fact]
    public void Refuses_production_from_a_branch_other_than_release()
    {
        ReleaseVersion("1.2.3", tag: "v1.2.3");
        Repo.Git("checkout", "--quiet", "main");
        Repo.Git("merge", "--quiet", "--ff-only", "release");
        Repo.Push("main");

        var run = Repo.Run("production");

        run.ExitCode.Should().NotBe(0);
        run.Output.Should().Contain("production deploys only from the release branch");
        run.Calls.Should().BeEmpty();
    }

    [Fact]
    public void Refuses_production_when_release_has_commits_origin_release_does_not()
    {
        ReleaseVersion("1.2.3", tag: "v1.2.3");
        Repo.Commit("not pushed");

        var run = Repo.Run("production");

        run.ExitCode.Should().NotBe(0);
        run.Output.Should().Contain("HEAD is not origin/release");
        run.Calls.Should().BeEmpty();
    }

    [Fact]
    public void Refuses_production_without_a_version_tag_on_HEAD()
    {
        ReleaseVersion("1.2.3", tag: null);

        var run = Repo.Run("production");

        run.ExitCode.Should().NotBe(0);
        run.Output.Should().Contain("HEAD carries no tag v1.2.3");
        run.Calls.Should().BeEmpty();
    }

    [Fact]
    public void Refuses_production_when_the_tag_does_not_match_the_csproj_version()
    {
        ReleaseVersion("1.2.4", tag: "v1.2.3");

        var run = Repo.Run("production");

        run.ExitCode.Should().NotBe(0);
        run.Output.Should().Contain("HEAD carries no tag v1.2.4").And.Contain("v1.2.3");
        run.Calls.Should().BeEmpty();
    }

    [Fact]
    public void Refuses_production_when_the_csproj_has_no_version()
    {
        ReleaseVersion(null, tag: "v1.0.0");

        var run = Repo.Run("production");

        run.ExitCode.Should().NotBe(0);
        run.Output.Should().Contain("<Version> must be X.Y.Z");
        run.Calls.Should().BeEmpty();
    }

    [Fact]
    public void Refuses_production_when_the_csproj_version_is_not_X_Y_Z()
    {
        ReleaseVersion("1.2", tag: "v1.2");

        var run = Repo.Run("production");

        run.ExitCode.Should().NotBe(0);
        run.Output.Should().Contain("<Version> must be X.Y.Z (found: '1.2')");
        run.Calls.Should().BeEmpty();
    }

    [Fact]
    public void Refuses_production_when_origin_holds_a_different_tag_of_that_name()
    {
        ReleaseVersion("1.2.3", tag: "v1.2.3");
        Repo.Git("tag", "-d", "v1.2.3");
        Repo.Git("tag", "-a", "v1.2.3", "-m", "re-tagged locally");

        var run = Repo.Run("production");

        run.ExitCode.Should().NotBe(0);
        run.Output.Should().Contain("origin's differs");
        run.Calls.Should().BeEmpty();
    }

    [Fact]
    public void Refuses_to_deploy_a_version_older_than_the_newest_tag_on_origin()
    {
        // Roll forward, never back (ADR-0002 §8): release went back to an older version after v1.3.0 shipped.
        ReleaseVersion("1.3.0", tag: "v1.3.0");
        Repo.SetVersion("1.2.9");
        Repo.Commit("back to 1.2.9");
        Repo.Git("tag", "-a", "v1.2.9", "-m", "v1.2.9");
        Repo.Push("release");
        Repo.Push("v1.2.9");

        var run = Repo.Run("production");

        run.ExitCode.Should().NotBe(0);
        run.Output.Should().Contain("v1.2.9 is older than v1.3.0 on origin");
        run.Calls.Should().BeEmpty();
    }

    [Fact]
    public void Refuses_production_when_the_tag_is_not_on_origin()
    {
        ReleaseVersion("1.2.3", tag: "v1.2.3", pushTag: false);

        var run = Repo.Run("production");

        run.ExitCode.Should().NotBe(0);
        run.Output.Should().Contain("tag v1.2.3 is not on origin");
        run.Calls.Should().BeEmpty();
    }
}
