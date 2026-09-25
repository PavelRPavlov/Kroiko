namespace Kroiko.Client.Blazor.Theme;

/// <summary>
/// Loads and saves the operator's <see cref="ThemeMode"/> on this device. The app keeps it in browser storage; the
/// tests fake the storage.
/// </summary>
public interface IThemeStore
{
    /// <summary>The remembered mode, or <see cref="ThemeMode.System"/> when there is none. Never throws.</summary>
    Task<ThemeMode> LoadAsync();

    /// <summary>
    /// Remembers <paramref name="mode"/> on this device. Never throws: a mode that cannot be saved still applies until
    /// the app is reloaded.
    /// </summary>
    Task SaveAsync(ThemeMode mode);
}
