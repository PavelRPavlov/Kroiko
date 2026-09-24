using FluentAssertions;
using Kroiko.Client.Blazor.Updates;
using Xunit;

namespace Kroiko.Client.Tests.Updates;

/// <summary>
/// The version About shows, <c>v0.1.0 (a1b2c3d)</c>, from the assembly's informational version
/// (docs/implementation/06-updates-and-about.md, step 1; ADR-0002 §5–6).
/// </summary>
public sealed class AppVersionTests
{
    [Theory]
    [InlineData("0.1.0+a1b2c3d4e5f60718293a4b5c6d7e8f9012345678", "v0.1.0 (a1b2c3d)")]
    [InlineData("1.0.0+a1b2c3d", "v1.0.0 (a1b2c3d)")]
    [InlineData("1.3.0-beta.1+a1b2c3d4e5f60718293a4b5c6d7e8f9012345678", "v1.3.0-beta.1 (a1b2c3d)")]
    [InlineData("0.1.0+abc", "v0.1.0 (abc)")]
    public void Shows_the_version_and_the_short_commit_sha(string informationalVersion, string shown) =>
        AppVersion.Format(informationalVersion).Should().Be(shown);

    [Theory]
    [InlineData("0.1.0", "v0.1.0")]
    [InlineData("0.1.0+", "v0.1.0")]
    public void Shows_only_the_version_without_a_commit_sha(string informationalVersion, string shown) =>
        AppVersion.Format(informationalVersion).Should().Be(shown);

    [Fact]
    public void The_app_carries_its_version_and_the_commit_it_was_built_from() =>
        // Source Link appends +<sha> to the csproj <Version> when the app is built from the git repo.
        AppVersion.Current.Should().MatchRegex(@"^v\d+\.\d+\.\d+ \([0-9a-f]{7}\)$");
}
