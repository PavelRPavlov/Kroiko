using FluentAssertions;
using Kroiko.Client.Blazor.Conversion;
using Kroiko.Client.Blazor.Theme;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Xunit;

namespace Kroiko.Client.Tests.Theme;

/// <summary>
/// The app's theme services resolve, over the browser storage the device settings use too (ADR-0014 §2;
/// docs/implementation/08-theme.md, step 1).
/// </summary>
public sealed class ThemeRegistrationTests
{
    [Fact]
    public void The_theme_and_the_device_settings_share_one_browser_storage()
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddScoped<IJSRuntime, UnusedJSRuntime>()
            .AddDeviceSettingsStore()
            .AddTheme();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        using var app = provider.CreateScope();

        app.ServiceProvider.GetRequiredService<IThemeStore>().Should().BeOfType<LocalStorageThemeStore>();
        app.ServiceProvider.GetRequiredService<BrowserTheme>().Should().NotBeNull();
        app.ServiceProvider.GetServices<IBrowserStorage>().Should().ContainSingle();
    }

    /// <summary>Resolving the services calls no JavaScript.</summary>
    private sealed class UnusedJSRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            throw new InvalidOperationException($"Unexpected call to '{identifier}'.");

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            throw new InvalidOperationException($"Unexpected call to '{identifier}'.");
    }
}
