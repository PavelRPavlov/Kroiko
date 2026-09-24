using Microsoft.Extensions.DependencyInjection;

namespace Kroiko.Client.Blazor.Conversion;

public static class ConversionServiceCollectionExtensions
{
    /// <summary>
    /// Registers the app's one <see cref="ConverterState"/> (ADR-0005 §4). It is scoped, which in Blazor
    /// WebAssembly means once for the app, and lets it use scoped services such as MudBlazor's dialogs. The
    /// app registers an <see cref="IConfirmation"/> and an <see cref="IDeviceSettingsStore"/> alongside it.
    /// </summary>
    public static IServiceCollection AddConverterState(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.AddScoped<ConverterState>();
    }

    /// <summary>
    /// Registers the <see cref="IDeviceSettingsStore"/> that keeps the device settings in the browser's
    /// <c>localStorage</c> (ADR-0002 §7–8). Scoped, like the <c>IJSRuntime</c> it calls.
    /// </summary>
    public static IServiceCollection AddDeviceSettingsStore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services
            .AddScoped<IBrowserStorage, BrowserLocalStorage>()
            .AddScoped<IDeviceSettingsStore, LocalStorageDeviceSettingsStore>();
    }
}
