namespace Kroiko.Client.Blazor.Theme;

/// <summary>How the app picks its light or dark palette (ADR-0014 §1). <see cref="System"/> is the default.</summary>
public enum ThemeMode
{
    /// <summary>Follows the device's light or dark setting, live.</summary>
    System,

    /// <summary>Always light.</summary>
    Light,

    /// <summary>Always dark.</summary>
    Dark,
}
