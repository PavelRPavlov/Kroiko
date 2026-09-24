using FluentAssertions;
using Xunit;

namespace Kroiko.Client.Tests.PublishScript;

/// <summary>What <c>scripts/publish-pwa.ps1</c> checks for every environment, and how it uses the AWS credentials.</summary>
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
        run.AwsCommands.Should().Equal(["sts get-caller-identity"], "nothing is uploaded or switched");
    }

    [Fact]
    public void Refuses_without_working_credentials_before_the_test_run()
    {
        var run = Repo.Run("main", new RunOptions { AwsFailsOn = "sts" });

        run.ExitCode.Should().NotBe(0);
        run.Output.Should().Contain("checking the credentials of the AWS CLI profile kroiko-pwa failed")
            .And.Contain("aws configure --profile kroiko-pwa");
        run.DotnetCalls.Should().BeEmpty("the long test run is not started for a deploy that cannot happen");
    }

    [Fact]
    public void Refuses_when_hosting_json_is_not_filled_in_before_running_anything()
    {
        Repo.WriteHostingConfig(bucket: "", mainDistribution: "", mainUrl: PublishScriptSandbox.MainUrl);
        Repo.Commit("hosting.json as committed before provisioning");
        Repo.Push("main");

        var run = Repo.Run("main");

        run.ExitCode.Should().NotBe(0);
        run.Output.Should().Contain("hosting/aws/hosting.json has no bucket, environments.main.distributionId");
        run.Calls.Should().BeEmpty();
    }

    [Fact]
    public void Passes_the_deploy_profile_and_region_to_every_aws_call()
    {
        // The key stays in the profile: the script never reads, prints or passes it.
        var run = Repo.Run("main");

        run.ExitCode.Should().Be(0, run.Output);
        run.AwsCalls.Should().NotBeEmpty()
            .And.OnlyContain(c => c.EndsWith($" --profile {PublishScriptSandbox.Profile} --region {PublishScriptSandbox.Region}"));
    }

    [Fact]
    public void A_dry_run_tests_and_publishes_then_prints_the_plan_without_calling_aws()
    {
        var run = Repo.Run("main", new RunOptions { DryRun = true });

        run.ExitCode.Should().Be(0, run.Output);
        run.DotnetCalls.Should().HaveCount(2);
        run.AwsCalls.Should().BeEmpty("a dry run needs neither the credentials nor the CLI");

        run.Output.Should().MatchRegex(@"Dry run: would deploy v1\.0\.0 \([0-9a-f]{7}\) to main - 8 files, raw 0 MB, br 0 MB:");
        run.Output.Should().MatchRegex($@"upload  s3://{PublishScriptSandbox.Bucket}/main/\d{{8}}T\d{{6}}Z-v1\.0\.0-[0-9a-f]{{7}}/ \(raw/ and br/, in \d+ uploads\)");
        run.Output.Should().MatchRegex($@"switch  {PublishScriptSandbox.MainDistribution} to origin path /main/\S+, then invalidate /\*");
        Directory.Exists(Path.GetDirectoryName(run.WebRoot)).Should().BeFalse("the temporary publish folder is removed");
    }

    [Fact]
    public void A_dry_run_works_before_hosting_json_is_filled_in()
    {
        Repo.WriteHostingConfig(bucket: "", mainDistribution: "", mainUrl: "");
        Repo.Commit("hosting.json as committed before provisioning");
        Repo.Push("main");

        var run = Repo.Run("main", new RunOptions { DryRun = true });

        run.ExitCode.Should().Be(0, run.Output);
        run.Output.Should().Contain("s3://(bucket not set)/main/").And.Contain("switch  (distributionId not set)");
    }

    [Fact]
    public void A_dry_run_still_enforces_the_branch_rules()
    {
        Repo.Commit("local only");

        var run = Repo.Run("main", new RunOptions { DryRun = true });

        run.ExitCode.Should().NotBe(0);
        run.Output.Should().Contain("HEAD is not origin/main");
        run.Calls.Should().BeEmpty();
    }
}
