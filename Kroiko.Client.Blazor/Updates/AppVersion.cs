using System.Reflection;

namespace Kroiko.Client.Blazor.Updates;

/// <summary>
/// The app's version as the About dialog shows it, <c>v0.1.0 (a1b2c3d)</c> (ADR-0002 §5). The SemVer is the csproj's
/// hand-maintained <c>&lt;Version&gt;</c>; the SDK's Source Link appends <c>+&lt;commit sha&gt;</c> to the assembly's
/// informational version (ADR-0002 §6).
/// </summary>
internal static class AppVersion
{
    // ADR-0002 §5's a1b2c3d. publish-pwa.ps1's `git rev-parse --short=7` agrees unless 7 characters are ambiguous.
    private const int ShortShaLength = 7;

    /// <summary>This build's version, e.g. <c>v0.1.0 (a1b2c3d)</c>; the SDK always emits the informational version.</summary>
    public static string Current { get; } = Format(
        typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion);

    /// <summary>
    /// Formats an informational version, <c>0.1.0+&lt;sha&gt;</c>, as <c>v0.1.0 (a1b2c3d)</c>: the short commit sha
    /// in brackets, or just <c>v0.1.0</c> without one.
    /// </summary>
    public static string Format(string informationalVersion)
    {
        var plus = informationalVersion.IndexOf('+');
        if (plus < 0)
            return $"v{informationalVersion}";

        var version = informationalVersion[..plus];
        var sha = informationalVersion[(plus + 1)..];
        return sha.Length == 0
            ? $"v{version}"
            : $"v{version} ({sha[..Math.Min(ShortShaLength, sha.Length)]})";
    }
}
