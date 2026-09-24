using Microsoft.Extensions.DependencyInjection;

namespace Kroiko.Client.Blazor.Updates;

public static class UpdatesServiceCollectionExtensions
{
    /// <summary>
    /// Registers the app's one <see cref="AppUpdates"/> (ADR-0002 §1, §3–4). Scoped, which in Blazor WebAssembly means
    /// once for the app, like the <c>IJSRuntime</c> it calls.
    /// </summary>
    public static IServiceCollection AddAppUpdates(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.AddScoped<AppUpdates>();
    }
}
