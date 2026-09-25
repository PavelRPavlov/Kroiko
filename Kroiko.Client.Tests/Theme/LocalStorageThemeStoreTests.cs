using FluentAssertions;
using Kroiko.Client.Blazor.Theme;
using Kroiko.Client.Tests.Conversion;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kroiko.Client.Tests.Theme;

/// <summary>
/// The theme in browser storage, as a plain string under <c>kroiko.theme</c> (ADR-0014 §2;
/// docs/implementation/08-theme.md, step 1). The storage is faked; E2E/ThemeTests covers the real browser.
/// </summary>
public sealed class LocalStorageThemeStoreTests
{
    private const string StorageKey = "kroiko.theme";

    private readonly FakeBrowserStorage _storage = new();

    [Fact]
    public async Task A_device_that_remembers_nothing_follows_the_system()
    {
        (await Store().LoadAsync()).Should().Be(ThemeMode.System);
    }

    [Theory]
    [InlineData(ThemeMode.System)]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public async Task A_saved_mode_loads_back(ThemeMode mode)
    {
        await Store().SaveAsync(mode);

        (await Store().LoadAsync()).Should().Be(mode);
    }

    [Theory]
    [InlineData(ThemeMode.System, "system")]
    [InlineData(ThemeMode.Light, "light")]
    [InlineData(ThemeMode.Dark, "dark")]
    public async Task A_mode_is_saved_as_the_plain_string_index_html_reads(ThemeMode mode, string stored)
    {
        await Store().SaveAsync(mode);

        _storage.Items[StorageKey].Should().Be(stored);
    }

    [Theory]
    [InlineData("sepia")]
    [InlineData("Dark")]
    [InlineData("")]
    public async Task A_value_the_app_does_not_know_follows_the_system(string stored)
    {
        _storage.Items[StorageKey] = stored;

        (await Store().LoadAsync()).Should().Be(ThemeMode.System);
    }

    [Fact]
    public async Task Storage_that_throws_on_load_follows_the_system()
    {
        _storage.Failure = new InvalidOperationException("localStorage is disabled.");

        (await Store().LoadAsync()).Should().Be(ThemeMode.System);
    }

    [Fact]
    public async Task Storage_that_throws_on_save_does_not_stop_the_pick()
    {
        _storage.Failure = new InvalidOperationException("The quota has been exceeded.");

        var save = () => Store().SaveAsync(ThemeMode.Dark);

        await save.Should().NotThrowAsync();
    }

    private LocalStorageThemeStore Store() => new(_storage, NullLogger<LocalStorageThemeStore>.Instance);
}
