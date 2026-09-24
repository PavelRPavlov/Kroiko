using Microsoft.Playwright;

namespace Kroiko.Client.Tests.E2E;

/// <summary>
/// Launches the Chromium pinned by the Microsoft.Playwright package (ADR-0007 §5): headless, or headed when
/// <c>HEADED=1</c>. A missing browser fails with the command that installs it.
/// </summary>
internal static class Chromium
{
    /// <summary>The one-time browser install, run from <c>Kroiko.Client.Tests/</c> after a build.</summary>
    public const string InstallCommand = "pwsh bin/Debug/net10.0/playwright.ps1 install chromium";

    public static bool IsHeadless(string? headed) => headed != "1";

    /// <param name="executablePath">A browser other than the bundled one; tests use it to simulate a missing browser.</param>
    public static async Task<IBrowser> LaunchAsync(IPlaywright playwright, string? executablePath = null)
    {
        try
        {
            return await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = IsHeadless(Environment.GetEnvironmentVariable("HEADED")),
                ExecutablePath = executablePath,
            });
        }
        catch (PlaywrightException e) when (e.Message.Contains("executable doesn't exist", StringComparison.OrdinalIgnoreCase))
        {
            var script = Path.Combine(AppContext.BaseDirectory, "playwright.ps1");
            throw new InvalidOperationException(
                $"The Playwright browser is not installed. Run once, from Kroiko.Client.Tests: {InstallCommand}" +
                $"{Environment.NewLine}(the script for this build: pwsh \"{script}\" install chromium)",
                e);
        }
    }
}
