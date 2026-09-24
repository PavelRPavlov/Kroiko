using System.Globalization;

namespace Kroiko.Testing;

/// <summary>
/// Runs the code inside a <c>using</c> block under another host culture (current and UI culture), and
/// restores the previous ones on dispose. Hosts and browsers may be <c>bg-BG</c>, whose decimal comma
/// is what the domain's invariant-culture rule guards against.
/// </summary>
public sealed class CultureScope : IDisposable
{
    private readonly CultureInfo _previousCulture = CultureInfo.CurrentCulture;
    private readonly CultureInfo _previousUiCulture = CultureInfo.CurrentUICulture;

    public CultureScope(string name)
    {
        var culture = CultureInfo.GetCultureInfo(name);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    /// <summary>A scope under <c>bg-BG</c> (decimal comma).</summary>
    public static CultureScope BgBg() => new("bg-BG");

    public void Dispose()
    {
        CultureInfo.CurrentCulture = _previousCulture;
        CultureInfo.CurrentUICulture = _previousUiCulture;
    }
}
