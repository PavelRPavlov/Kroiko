using Kroiko.Client.Blazor.Conversion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kroiko.Client.Blazor.Theme;

public static class ThemeServiceCollectionExtensions
{
    /// <summary>
    /// Registers the <see cref="IThemeStore"/> that keeps the theme in the browser's <c>localStorage</c> and the
    /// <see cref="BrowserTheme"/> (ADR-0014 §2–3). Scoped, like the <c>IJSRuntime</c> they call.
    /// </summary>
    public static IServiceCollection AddTheme(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<IBrowserStorage, BrowserLocalStorage>();
        return services
            .AddScoped<IThemeStore, LocalStorageThemeStore>()
            .AddScoped<BrowserTheme>();
    }
}
