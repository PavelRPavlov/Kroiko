using FluentAssertions;
using Xunit;

namespace Kroiko.Client.Tests.PublishScript;

/// <summary>What <c>scripts/publish-pwa.ps1</c> checks for every environment, and how it treats the token.</summary>
public sealed class PublishScriptSafetyTests(PublishScriptTemplate template) : PublishScriptTestBase(template)
{
    [Fact]
    public void Refuses_a_working_tree_with_uncommitted_changes()
    {
        File.WriteAllText(Path.Combine(Repo.WorkTree, "notes.txt"), "untracked");

        var run = Repo.Run("main");

        run.ExitCode.Should().NotBe(0);
        run.Output.Should().Contain("working tree is not clean");
        run.Calls.Should().BeEmpty("nothing is tested, published or deployed");
    }

    [Fact]
    public void Refuses_when_the_tests_fail()
    {
        var run = Repo.Run("main", new RunOptions { TestsExitCode = 1 });

        run.ExitCode.Should().NotBe(0);
        run.Output.Should().Contain("dotnet test failed");
        run.DotnetCalls.Should().ContainSingle().Which.Should().StartWith("dotnet test ");
        run.SwaCalls.Should().BeEmpty();
    }

    [Fact]
    public void Refuses_without_a_deployment_token_before_running_anything()
    {
        var run = Repo.Run("main", new RunOptions { WithToken = false });

        run.ExitCode.Should().NotBe(0);
        run.Output.Should().Contain("SWA_CLI_DEPLOYMENT_TOKEN is not set");
        run.Calls.Should().BeEmpty();
    }

    [Fact]
    public void Hands_the_token_to_swa_only_through_the_environment_and_never_prints_it()
    {
        // SWA_CLI_DEBUG=silly makes the real CLI log the token; the stub echoes it regardless.
        var run = Repo.Run("main", new RunOptions { SwaCliDebug = "silly" });

        run.ExitCode.Should().Be(0, run.Output);
        run.Calls.Should().Contain("swa-token-present True").And.Contain("swa-debug ''");
        run.Calls.Where(c => c.StartsWith("dotnet-token-present", StringComparison.Ordinal))
            .Should().Equal(["dotnet-token-present False", "dotnet-token-present False"], "the tests and the build never see it");
        run.SwaCalls.Should().ContainSingle().Which.Should().NotContain(Repo.Token);
        run.Output.Should().NotContain(Repo.Token);
    }

    [Fact]
    public void Fails_when_swa_deploy_fails()
    {
        var run = Repo.Run("main", new RunOptions { SwaExitCode = 1 });

        run.ExitCode.Should().NotBe(0);
        run.Output.Should().Contain("swa deploy failed");
        Directory.Exists(Path.GetDirectoryName(run.WebRoot)).Should().BeFalse("the temporary publish folder is removed");
    }

    [Fact]
    public void Fails_when_swa_reports_no_deployed_url()
    {
        // The real CLI returns 0 on some failures (a failed login, a missing folder) and reports success only by the URL.
        var run = Repo.Run("main", new RunOptions { SwaPrintsUrl = false });

        run.ExitCode.Should().NotBe(0);
        run.Output.Should().Contain("swa deploy reported no deployed URL");
    }

    [Fact]
    public void A_dry_run_tests_and_publishes_then_prints_the_deploy_command_without_a_token()
    {
        var run = Repo.Run("main", new RunOptions { DryRun = true, WithToken = false });

        run.ExitCode.Should().Be(0, run.Output);
        run.DotnetCalls.Should().HaveCount(2);
        run.SwaCalls.Should().BeEmpty("a dry run deploys nothing");

        run.Output.Should().Contain(run.ExpectedSwaDeploy("main"));
    }

    [Fact]
    public void A_dry_run_still_enforces_the_branch_rules()
    {
        Repo.Commit("local only");

        var run = Repo.Run("main", new RunOptions { DryRun = true, WithToken = false });

        run.ExitCode.Should().NotBe(0);
        run.Output.Should().Contain("HEAD is not origin/main");
        run.Calls.Should().BeEmpty();
    }
}
