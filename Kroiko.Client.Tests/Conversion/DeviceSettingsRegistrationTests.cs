using System.Text.Json;
using FluentAssertions;
using Kroiko.Client.Blazor.Conversion;
using Kroiko.Domain;
using Kroiko.Domain.CellsExtracting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Xunit;

namespace Kroiko.Client.Tests.Conversion;

/// <summary>
/// The app's device settings go to the browser's <c>localStorage</c> (docs/implementation/04-conversion-flow.md,
/// step 2). The JS runtime is faked; the Playwright "device storage" scenario (step 6) covers the real browser.
/// </summary>
public sealed class DeviceSettingsRegistrationTests
{
    [Fact]
    public async Task The_app_keeps_the_device_settings_in_localStorage()
    {
        var localStorage = new FakeLocalStorageRuntime();
        var services = new ServiceCollection()
            .AddLogging()
            .AddScoped<IJSRuntime>(_ => localStorage)
            .AddDeviceSettingsStore();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        using var app = provider.CreateScope();
        var store = app.ServiceProvider.GetRequiredService<IDeviceSettingsStore>();
        var saved = new DeviceSettings(new ContactInfo("Мебели Ангелов ЕООД", "0888 123 456"), SupportedCompanies.Lonira);

        await store.SaveAsync(saved);

        localStorage.Items.Should().ContainKey("kroiko.deviceSettings");
        (await store.LoadAsync()).Should().Be(saved);
    }

    /// <summary>A JS runtime that knows only <c>localStorage.getItem</c> and <c>localStorage.setItem</c>.</summary>
    private sealed class FakeLocalStorageRuntime : IJSRuntime
    {
        public Dictionary<string, string> Items { get; } = [];

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            switch (identifier)
            {
                case "localStorage.getItem":
                    var value = Items.GetValueOrDefault((string)args![0]!);
                    // What the browser hands back is JSON, deserialized into the requested type.
                    return ValueTask.FromResult(JsonSerializer.Deserialize<TValue>(JsonSerializer.Serialize(value))!);
                case "localStorage.setItem":
                    Items[(string)args![0]!] = (string)args[1]!;
                    return ValueTask.FromResult(default(TValue)!);
                default:
                    throw new JSException($"Could not find '{identifier}'.");
            }
        }
    }
}
