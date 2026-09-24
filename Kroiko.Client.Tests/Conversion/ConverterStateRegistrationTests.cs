using FluentAssertions;
using Kroiko.Client.Blazor.Conversion;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Kroiko.Client.Tests.Conversion;

/// <summary>The Order is app-wide, so it survives in-app navigation (ADR-0005 §4).</summary>
public sealed class ConverterStateRegistrationTests
{
    [Fact]
    public void Every_page_of_the_app_gets_the_same_Order()
    {
        var services = new ServiceCollection()
            .AddScoped<IConfirmation, FakeConfirmation>()
            .AddScoped<IDeviceSettingsStore, FakeDeviceSettingsStore>()
            .AddConverterState();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        // Blazor WebAssembly resolves every component's services from one scope for the app's lifetime.
        using var app = provider.CreateScope();
        var converterPage = app.ServiceProvider.GetRequiredService<ConverterState>();
        var configurationPage = app.ServiceProvider.GetRequiredService<ConverterState>();

        configurationPage.Should().BeSameAs(converterPage);
    }
}
