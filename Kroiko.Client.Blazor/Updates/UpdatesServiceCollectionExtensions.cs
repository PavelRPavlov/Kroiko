using Microsoft.Extensions.DependencyInjection;

namespace Kroiko.Client.Blazor.Updates;

public static class UpdatesServiceCollectionExtensions
{
    /// <summary>
    /// Registers the app's one <see cref="AppUpdates"/> and the one <see cref="UpdateOffer"/> the snackbar and About share
    /// (ADR-0002 §1–4). Scoped, which in Blazor WebAssembly means once for the app, like the <c>IJSRuntime</c> they call.
    /// The offer asks through the app's <c>IConfirmation</c> before reloading over the <c>ConverterState</c>'s unsaved
    /// work, so the app registers those too.
    /// </summary>
    public static IServiceCollection AddAppUpdates(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services
            .AddScoped<AppUpdates>()
            .AddScoped<UpdateOffer>();
    }
}
