using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace Kroiko.Client.Tests.E2E;

/// <summary>
/// A missing browser fails the E2E tests with the command that installs it. Needs the Playwright driver
/// (shipped in the package) but no browser, so it runs in the fast loop.
/// </summary>
public sealed class ChromiumTests
{
    [Fact]
    public async Task A_missing_browser_fails_with_the_install_command()
    {
        using var playwright = await Playwright.CreateAsync();
        var missing = Path.Combine(Path.GetTempPath(), $"kroiko-no-browser-{Guid.NewGuid():N}", "chrome.exe");

        var launch = () => Chromium.LaunchAsync(playwright, missing);

        (await launch.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*pwsh bin/Debug/net10.0/playwright.ps1 install chromium*")
            .WithInnerException<PlaywrightException>();
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("0", true)]
    [InlineData("1", false)]
    public void Runs_headless_unless_HEADED_is_1(string? headed, bool headless) =>
        Chromium.IsHeadless(headed).Should().Be(headless);
}
