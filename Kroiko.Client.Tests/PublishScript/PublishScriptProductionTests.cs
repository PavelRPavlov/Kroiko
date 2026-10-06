using FluentAssertions;
using Xunit;

namespace Kroiko.Client.Tests.PublishScript;

/// <summary>
/// <c>-Environment production</c>: only the commit tagged <c>vX.Y.Z</c> — the pushed tag (<c>-Tag</c>), or the csproj's
/// without it — whose csproj <c>&lt;Version&gt;</c> is X.Y.Z, and which is on <c>origin/release</c> (ADR-0010,
/// ADR-0002 §6, ADR-0017).
/// </summary>
public sealed class PublishScriptProductionTests(PublishScriptTemplate template) : PublishScriptTestBase(template)
{
    [Fact]
    public void Deploys_a_tagged_release_to_the_production_distribution()
    {
        ReleaseVersion("1.2.3", tag: "v1.2.3");
        var shortSha = Repo.Git("rev-parse", "--short=7", "HEAD");

        var run = Repo.Run("production");

        run.ExitCode.Should().Be(0, run.Output);
        run.DotnetCalls.First().Should().StartWith("dotnet test ");
        run.OriginPath.Should().MatchRegex($@"^/production/\d{{8}}T\d{{6}}Z-v1\.2\.3-{shortSha}$");
        run.Uploads.Should().OnlyContain(upload => upload.Key.StartsWith("production/"));
        run.AwsCalls.Where(c => c.Contains(" --id ") || c.Contains(" --distribution-id "))
            .Should().NotBeEmpty().And.OnlyContain(c => c.Contains(PublishScriptSandbox.ProductionDistribution));
        run.AwsCalls.Should().Contain(c => c.StartsWith("aws cloudfront get-function --name kroiko-pwa-production "));
        run.Output.Should().Contain($"Deployed v1.2.3 ({shortSha}) to production: https://kroiko.com");
    }

    [Fact]
    public void Deploys_the_pushed_tag_when_release_has_moved_past_it()
    {
        // What the workflow does (ADR-0017): v1.2.3 tags main's merge, and release's tip is a later merge commit.
        ReleaseVersion("1.2.3", tag: "v1.2.3");
        var tagged = Repo.Git("rev-parse", "--short=7", "HEAD");
        Repo.Commit("Merge branch 'main' into release");
        Repo.Push("release");
        Repo.Git("checkout", "--quiet", "--detach", "v1.2.3");

        var run = Repo.Run("production", new RunOptions { Tag = "v1.2.3" });

        run.ExitCode.Should().Be(0, run.Output);
        run.OriginPath.Should().MatchRegex($@"-v1\.2\.3-{tagged}$");
        run.Output.Should().Contain($"Deployed v1.2.3 ({tagged}) to production");
    }

    [Fact]
    public void Refuses_the_pushed_tag_when_its_commits_csproj_has_another_version()
    {
        // The failed v0.1.2 deploy: the tag was pushed, but <Version> was not bumped.
        ReleaseVersion("1.2.3", tag: "v1.2.4");
        Repo.Git("checkout", "--quiet", "--detach", "v1.2.4");

        var run = Repo.Run("production", new RunOptions { Tag = "v1.2.4" });

        run.ExitCode.Should().NotBe(0);
        run.Output.Should().Contain("tag v1.2.4 does not match the csproj <Version> 1.2.3")
            .And.Contain("bump <Version> to 1.2.4");
        run.Calls.Should().BeEmpty();
    }

    [Fact]
    public void Refuses_the_pushed_tag_when_HEAD_is_not_its_commit()
    {
        ReleaseVersion("1.2.3", tag: "v1.2.3");
        Repo.Commit("after the tag");
        Repo.Push("release");

        var run = Repo.Run("production", new RunOptions { Tag = "v1.2.3" });

        run.ExitCode.Should().NotBe(0);
        run.Output.Should().Contain("HEAD carries no tag v1.2.3").And.Contain("git checkout v1.2.3");
        run.Calls.Should().BeEmpty();
    }

    [Theory]
    [InlineData("v1.2")]
    [InlineData("1.2.3")]
    [InlineData("v1.2.3-rc1")]
    public void Refuses_a_pushed_tag_that_is_not_a_release_tag(string tag)
    {
        ReleaseVersion("1.2.3", tag: "v1.2.3");

        var run = Repo.Run("production", new RunOptions { Tag = tag });

        run.ExitCode.Should().NotBe(0);
        run.Output.Should().Contain($"-Tag must be a release tag vX.Y.Z (found: '{tag}')");
        run.Calls.Should().BeEmpty();
    }

    [Fact]
    public void Refuses_a_tag_on_a_commit_that_is_not_on_release()
    {
        // Tagged on main, never merged into release.
        ReleaseVersion("1.2.3", tag: "v1.2.3");
        Repo.Git("checkout", "--quiet", "main");
        Repo.SetVersion("1.2.4");
        Repo.Commit("1.2.4 on main only");
        Repo.Push("main");
        Repo.Git("tag", "-a", "v1.2.4", "-m", "v1.2.4");
        Repo.Push("v1.2.4");

        var run = Repo.Run("production", new RunOptions { Tag = "v1.2.4" });

        run.ExitCode.Should().NotBe(0);
        run.Output.Should().Contain("the commit tagged v1.2.4 is not on origin/release");
        run.Calls.Should().BeEmpty();
    }

    [Fact]
    public void Refuses_a_tagged_release_commit_that_origin_release_does_not_have()
    {
        ReleaseVersion("1.2.3", tag: "v1.2.3");
        Repo.SetVersion("1.2.4");
        Repo.Commit("not pushed");
        Repo.Git("tag", "-a", "v1.2.4", "-m", "v1.2.4");
        Repo.Push("v1.2.4");

        var run = Repo.Run("production");

        run.ExitCode.Should().NotBe(0);
        run.Output.Should().Contain("the commit tagged v1.2.4 is not on origin/release");
        run.Calls.Should().BeEmpty();
    }

    [Fact]
    public void Tag_is_for_production_only()
    {
        var run = Repo.Run("main", new RunOptions { Tag = "v1.2.3" });

        run.ExitCode.Should().NotBe(0);
        run.Output.Should().Contain("-Tag is for -Environment production only");
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
