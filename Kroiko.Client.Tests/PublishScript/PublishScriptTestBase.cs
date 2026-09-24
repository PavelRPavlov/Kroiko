using Xunit;

namespace Kroiko.Client.Tests.PublishScript;

/// <summary>
/// <c>scripts/publish-pwa.ps1</c> enforces the branch model (ADR-0010, from ADR-0001) before it deploys
/// (docs/implementation/07-hosting-and-go-live.md, step 07a.2). Each test runs the real script against its own
/// sandbox git repository; <c>dotnet</c> and <c>aws</c> are stubs, so nothing is built, tested or deployed.
/// The tests are split over a few classes only so xUnit runs them in parallel.
/// </summary>
public abstract class PublishScriptTestBase : IClassFixture<PublishScriptTemplate>, IDisposable
{
    private protected PublishScriptTestBase(PublishScriptTemplate template) => Repo = new PublishScriptSandbox(template);

    private protected PublishScriptSandbox Repo { get; }

    public void Dispose() => Repo.Dispose();

    /// <summary>Sets the csproj version on a new <c>release</c> branch, tags it, and pushes both to origin.</summary>
    private protected void ReleaseVersion(string? version, string? tag, bool pushTag = true)
    {
        Repo.Git("checkout", "--quiet", "-b", "release");
        Repo.SetVersion(version);
        Repo.Commit($"release {version}");
        Repo.Push("release");
        if (tag is null)
            return;

        Repo.Git("tag", "-a", tag, "-m", tag);
        if (pushTag)
            Repo.Push(tag);
    }
}
