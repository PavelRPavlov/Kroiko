using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kroiko.Client.Blazor.Conversion;

public static class ConversionServiceCollectionExtensions
{
    /// <summary>
    /// Registers the app's one <see cref="ConverterState"/> (ADR-0005 §4). It is scoped, which in Blazor
    /// WebAssembly means once for the app, and lets it use scoped services such as MudBlazor's dialogs. The
    /// app registers an <see cref="IConfirmation"/>, an <see cref="IDeviceSettingsStore"/>, an
    /// <see cref="IFileDownloader"/> and an <see cref="IFolderPicker"/> alongside it.
    /// </summary>
    public static IServiceCollection AddConverterState(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.AddScoped<ConverterState>();
    }

    /// <summary>
    /// Registers the <see cref="IConfirmation"/> that asks with a MudBlazor dialog (ADR-0005 §5). Scoped, like
    /// the <c>IDialogService</c> it shows the dialog through; the app also calls <c>AddMudServices()</c>.
    /// </summary>
    public static IServiceCollection AddConfirmationDialog(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.AddScoped<IConfirmation, MudDialogConfirmation>();
    }

    /// <summary>
    /// Registers the <see cref="IDeviceSettingsStore"/> that keeps the device settings in the browser's
    /// <c>localStorage</c> (ADR-0002 §7–8). Scoped, like the <c>IJSRuntime</c> it calls.
    /// </summary>
    public static IServiceCollection AddDeviceSettingsStore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<IBrowserStorage, BrowserLocalStorage>();
        return services.AddScoped<IDeviceSettingsStore, LocalStorageDeviceSettingsStore>();
    }

    /// <summary>
    /// Registers the <see cref="IFileDownloader"/> that hands files to the browser as downloads (ADR-0003 §5).
    /// Scoped, like the <c>IJSRuntime</c> it calls.
    /// </summary>
    public static IServiceCollection AddFileDownloader(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<FilesModule>();
        return services.AddScoped<IFileDownloader, BrowserFileDownloader>();
    }

    /// <summary>
    /// Registers the <see cref="IFolderPicker"/> that opens the browser's folder picker and writes into the picked
    /// folder (ADR-0003 §2–3). Scoped, like the <c>IJSRuntime</c> it calls.
    /// </summary>
    public static IServiceCollection AddFolderPicker(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<FilesModule>();
        return services.AddScoped<IFolderPicker, BrowserFolderPicker>();
    }
}
