using System.Text.Json.Nodes;
using FluentAssertions;
using Kroiko.Client.Blazor.Conversion;
using Kroiko.Domain;
using Kroiko.Domain.CellsExtracting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kroiko.Client.Tests.Conversion;

/// <summary>
/// The device settings in browser storage, with a <c>schemaVersion</c> (ADR-0002 §7–8;
/// docs/implementation/04-conversion-flow.md, step 2). The storage is faked; only its two calls are interop.
/// </summary>
public sealed class LocalStorageDeviceSettingsStoreTests
{
    private readonly FakeBrowserStorage _storage = new();

    [Fact]
    public async Task A_device_that_remembers_nothing_loads_the_defaults()
    {
        var settings = await Store().LoadAsync();

        settings.Should().Be(DeviceSettings.Default);
    }

    [Fact]
    public async Task The_saved_contacts_and_manufacturer_load_back()
    {
        var saved = new DeviceSettings(new ContactInfo("Мебели Ангелов ЕООД", "0888 123 456"), SupportedCompanies.Suliver);

        await Store().SaveAsync(saved);
        var loaded = await Store().LoadAsync();

        loaded.Should().Be(saved);
    }

    [Fact]
    public async Task Settings_are_saved_with_schema_version_1()
    {
        await Store().SaveAsync(new DeviceSettings(new ContactInfo("Мебели Ангелов ЕООД", "0888 123 456"), SupportedCompanies.Lonira));

        var stored = JsonNode.Parse(_storage.Items[StorageKey])!;
        ((int)stored["schemaVersion"]!).Should().Be(1);
    }

    [Fact]
    public async Task Schema_version_1_settings_stored_by_an_earlier_visit_load()
    {
        _storage.Items[StorageKey] =
            """{"schemaVersion":1,"companyName":"Мебели Ангелов ЕООД","mobileNumber":"0888 123 456","manufacturer":"MegaTrading"}""";

        var loaded = await Store().LoadAsync();

        loaded.Should().Be(new DeviceSettings(new ContactInfo("Мебели Ангелов ЕООД", "0888 123 456"), SupportedCompanies.MegaTrading));
    }

    [Fact]
    public async Task A_manufacturer_the_app_does_not_know_loads_as_none_and_keeps_the_contacts()
    {
        _storage.Items[StorageKey] =
            """{"schemaVersion":1,"companyName":"Мебели Ангелов ЕООД","mobileNumber":"0888 123 456","manufacturer":"Egger"}""";

        var loaded = await Store().LoadAsync();

        loaded.Should().Be(new DeviceSettings(new ContactInfo("Мебели Ангелов ЕООД", "0888 123 456"), null));
    }

    [Fact]
    public async Task Settings_from_a_newer_app_load_the_defaults_and_stay_in_storage()
    {
        const string newer =
            """{"schemaVersion":2,"companyName":"Мебели Ангелов ЕООД","mobileNumber":"0888 123 456","manufacturer":"Lonira"}""";
        _storage.Items[StorageKey] = newer;

        var loaded = await Store().LoadAsync();

        loaded.Should().Be(DeviceSettings.Default);
        _storage.Items[StorageKey].Should().Be(newer);
        _storage.Writes.Should().Be(0);
    }

    [Fact]
    public async Task Saving_after_settings_from_a_newer_app_replaces_them()
    {
        _storage.Items[StorageKey] =
            """{"schemaVersion":2,"companyName":"Мебели Ангелов ЕООД","mobileNumber":"0888 123 456","manufacturer":"Lonira"}""";
        var edited = new DeviceSettings(new ContactInfo("Кухни Петров", "0877 000 111"), SupportedCompanies.Suliver);

        await Store().SaveAsync(edited);

        ((int)JsonNode.Parse(_storage.Items[StorageKey])!["schemaVersion"]!).Should().Be(1);
        (await Store().LoadAsync()).Should().Be(edited);
    }

    [Fact]
    public async Task Storage_that_throws_loads_the_defaults()
    {
        _storage.Failure = new InvalidOperationException("localStorage is disabled.");

        var loaded = await Store().LoadAsync();

        loaded.Should().Be(DeviceSettings.Default);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    [InlineData("null")]
    [InlineData("""{"companyName":"Мебели Ангелов ЕООД"}""")]
    [InlineData("""{"schemaVersion":"1","companyName":"Мебели Ангелов ЕООД"}""")]
    [InlineData("""{"schemaVersion":0,"companyName":"Мебели Ангелов ЕООД"}""")]
    [InlineData("""{"schemaVersion":1,"companyName":42,"mobileNumber":"0888 123 456"}""")]
    [InlineData("""{"schemaVersion":1,"schemaVersion":1,"companyName":"Мебели Ангелов ЕООД"}""")]
    public async Task Stored_settings_the_app_cannot_read_load_the_defaults(string stored)
    {
        _storage.Items[StorageKey] = stored;

        var loaded = await Store().LoadAsync();

        loaded.Should().Be(DeviceSettings.Default);
        _storage.Items[StorageKey].Should().Be(stored);
    }

    // The app has no migration yet (schemaVersion 1 is the first), so these rules run on an imagined history:
    // version 2 split "contact" into two fields, version 3 renamed "company" to "manufacturer".
    private static readonly DeviceSettingsMigration[] ImaginedMigrations =
    [
        document =>
        {
            var contact = ((string)document["contact"]!).Split('|');
            document.Remove("contact");
            document["companyName"] = contact[0];
            document["mobileNumber"] = contact[1];
        },
        document =>
        {
            var company = document["company"];
            document.Remove("company");
            document["manufacturer"] = company;
        },
    ];

    [Theory]
    [InlineData("""{"schemaVersion":1,"contact":"Мебели Ангелов ЕООД|0888 123 456","company":"Lonira"}""")]
    [InlineData("""{"schemaVersion":2,"companyName":"Мебели Ангелов ЕООД","mobileNumber":"0888 123 456","company":"Lonira"}""")]
    [InlineData("""{"schemaVersion":3,"companyName":"Мебели Ангелов ЕООД","mobileNumber":"0888 123 456","manufacturer":"Lonira"}""")]
    public async Task Older_settings_pass_through_each_later_migration_in_order_before_they_are_read(string stored)
    {
        _storage.Items[StorageKey] = stored;

        var loaded = await StoreWith(ImaginedMigrations).LoadAsync();

        loaded.Should().Be(new DeviceSettings(new ContactInfo("Мебели Ангелов ЕООД", "0888 123 456"), SupportedCompanies.Lonira));
    }

    [Fact]
    public async Task With_migrations_the_settings_are_saved_with_the_latest_schema_version()
    {
        await StoreWith(ImaginedMigrations).SaveAsync(DeviceSettings.Default);

        ((int)JsonNode.Parse(_storage.Items[StorageKey])!["schemaVersion"]!).Should().Be(3);
    }

    [Fact]
    public async Task A_migration_that_fails_loads_the_defaults_and_leaves_storage_untouched()
    {
        const string stored = """{"schemaVersion":1,"contact":"no separator","company":"Lonira"}""";
        _storage.Items[StorageKey] = stored;

        var loaded = await StoreWith(ImaginedMigrations).LoadAsync();

        loaded.Should().Be(DeviceSettings.Default);
        _storage.Items[StorageKey].Should().Be(stored);
    }

    private const string StorageKey = "kroiko.deviceSettings";

    private LocalStorageDeviceSettingsStore StoreWith(IReadOnlyList<DeviceSettingsMigration> migrations) =>
        new(_storage, NullLogger<LocalStorageDeviceSettingsStore>.Instance, migrations);

    private LocalStorageDeviceSettingsStore Store() =>
        new(_storage, NullLogger<LocalStorageDeviceSettingsStore>.Instance);
}
