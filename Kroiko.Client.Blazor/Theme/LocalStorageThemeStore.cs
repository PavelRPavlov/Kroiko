using Kroiko.Client.Blazor.Conversion;
using Microsoft.Extensions.Logging;

namespace Kroiko.Client.Blazor.Theme;

/// <summary>
/// The <see cref="ThemeMode"/> in the browser's <c>localStorage</c>, as a plain string under <c>kroiko.theme</c>:
/// <c>system</c>, <c>light</c> or <c>dark</c> (ADR-0014 §2). It is kept apart from the device settings, which are saved
/// only by a successful generation; a theme is saved the moment it is picked.
/// <para>
/// wwwroot/index.html reads the same key before Blazor boots, by the same rules: <c>light</c> and <c>dark</c> are
/// picks, anything else is <see cref="ThemeMode.System"/>. Storage that is missing or throws loads
/// <see cref="ThemeMode.System"/>; a save that throws is logged.
/// </para>
/// </summary>
public sealed class LocalStorageThemeStore(IBrowserStorage storage, ILogger<LocalStorageThemeStore> logger) : IThemeStore
{
    internal const string Key = "kroiko.theme";

    public async Task<ThemeMode> LoadAsync()
    {
        try
        {
            return await storage.GetItemAsync(Key) switch
            {
                "light" => ThemeMode.Light,
                "dark" => ThemeMode.Dark,
                _ => ThemeMode.System,
            };
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "The theme could not be loaded; following the system.");
            return ThemeMode.System;
        }
    }

    public async Task SaveAsync(ThemeMode mode)
    {
        var stored = mode switch
        {
            ThemeMode.Light => "light",
            ThemeMode.Dark => "dark",
            ThemeMode.System => "system",
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Not a theme mode."),
        };
        try
        {
            await storage.SetItemAsync(Key, stored);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "The theme {Mode} could not be saved; it applies until the app is reloaded.", mode);
        }
    }
}
