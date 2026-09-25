using Microsoft.JSInterop;

namespace Kroiko.Client.Blazor.Theme;

/// <summary>
/// The page side of the theme (ADR-0014 §3): the device's light or dark setting, and the <c>data-theme</c> attribute on
/// <c>&lt;html&gt;</c> that wwwroot/index.html sets before Blazor boots and app.css styles the page background by.
/// </summary>
public sealed class BrowserTheme(IJSRuntime js)
{
    /// <summary>Whether the device is set to dark: <c>prefers-color-scheme: dark</c>.</summary>
    public async Task<bool> PrefersDarkAsync()
    {
        await using var query = await js.InvokeAsync<IJSObjectReference>("matchMedia", "(prefers-color-scheme: dark)");
        return await query.GetValueAsync<bool>("matches");
    }

    /// <summary>Sets <c>&lt;html data-theme&gt;</c> to the palette the app shows, so the page around it matches.</summary>
    public ValueTask ShowAsync(bool isDark) =>
        js.InvokeVoidAsync("document.documentElement.setAttribute", "data-theme", isDark ? "dark" : "light");
}
