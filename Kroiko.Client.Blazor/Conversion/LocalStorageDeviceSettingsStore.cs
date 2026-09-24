using System.Text.Json.Nodes;
using Kroiko.Domain;
using Microsoft.Extensions.Logging;

namespace Kroiko.Client.Blazor.Conversion;

/// <summary>
/// Upgrades a stored device-settings document by one <c>schemaVersion</c>, in place. The store sets the new
/// <c>schemaVersion</c> itself.
/// </summary>
public delegate void DeviceSettingsMigration(JsonObject document);

/// <summary>
/// The <see cref="DeviceSettings"/> in the browser's <c>localStorage</c>, as one JSON document under
/// <c>kroiko.deviceSettings</c> that carries a <c>schemaVersion</c> (ADR-0002 §7–8):
/// <code>{"schemaVersion":1,"companyName":"…","mobileNumber":"…","manufacturer":"Lonira"}</code>
/// The manufacturer is kept as its <see cref="Kroiko.Domain.CellsExtracting.SupportedCompany.Name"/>; a name this
/// app does not know loads as none.
/// <para>
/// Loading never throws. Older documents pass through the forward migrations before anything is read. A document
/// from a newer app, one this app cannot read, and storage that is missing or throws all load
/// <see cref="DeviceSettings.Default"/> and leave storage as it is; only saving (the operator's next successful
/// generation) replaces it. Saving throws what the storage throws; <see cref="ConverterState"/> reports it.
/// </para>
/// </summary>
public sealed class LocalStorageDeviceSettingsStore : IDeviceSettingsStore
{
    internal const string Key = "kroiko.deviceSettings";

    /// <summary>
    /// The forward migrations, oldest first: the one at index <c>i</c> upgrades <c>schemaVersion</c> <c>i + 1</c>
    /// to <c>i + 2</c>. None yet — <c>schemaVersion</c> 1 is the first. Changing the stored shape appends one here.
    /// </summary>
    private static readonly IReadOnlyList<DeviceSettingsMigration> Migrations = [];

    private readonly IBrowserStorage _storage;
    private readonly ILogger<LocalStorageDeviceSettingsStore> _logger;
    private readonly IReadOnlyList<DeviceSettingsMigration> _migrations;

    public LocalStorageDeviceSettingsStore(IBrowserStorage storage, ILogger<LocalStorageDeviceSettingsStore> logger)
        : this(storage, logger, Migrations)
    {
    }

    /// <summary>A store with other <paramref name="migrations"/>, for the tests of the migration rules.</summary>
    internal LocalStorageDeviceSettingsStore(
        IBrowserStorage storage,
        ILogger<LocalStorageDeviceSettingsStore> logger,
        IReadOnlyList<DeviceSettingsMigration> migrations)
    {
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(migrations);
        _storage = storage;
        _logger = logger;
        _migrations = migrations;
    }

    /// <summary>The <c>schemaVersion</c> this app writes, and the newest it can read.</summary>
    private int SchemaVersion => _migrations.Count + 1;

    public async Task<DeviceSettings> LoadAsync()
    {
        try
        {
            var json = await _storage.GetItemAsync(Key);
            return json is null ? DeviceSettings.Default : Read(json);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "The device settings could not be loaded; using the defaults.");
            return DeviceSettings.Default;
        }
    }

    public async Task SaveAsync(DeviceSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var document = new JsonObject
        {
            ["schemaVersion"] = SchemaVersion,
            ["companyName"] = settings.Contact.CompanyName,
            ["mobileNumber"] = settings.Contact.MobileNumber,
            ["manufacturer"] = settings.Manufacturer?.Name,
        };
        await _storage.SetItemAsync(Key, document.ToJsonString());
    }

    // Throws when the document cannot be read; LoadAsync turns that into the defaults.
    private DeviceSettings Read(string json)
    {
        var document = JsonNode.Parse(json) as JsonObject
            ?? throw new FormatException("The stored device settings are not a JSON object.");
        var schemaVersion = document["schemaVersion"] is JsonValue version && version.TryGetValue<int>(out var number)
            ? number
            : throw new FormatException("The stored device settings have no numeric schemaVersion.");
        if (schemaVersion < 1)
        {
            throw new FormatException($"The stored device settings have schemaVersion {schemaVersion}.");
        }

        if (schemaVersion > SchemaVersion)
        {
            // Roll forward, never back: a newer app wrote these, so leave them for it (ADR-0002 §8).
            _logger.LogInformation(
                "The stored device settings are schemaVersion {Stored}, newer than this app's {Known}; using the defaults.",
                schemaVersion, SchemaVersion);
            return DeviceSettings.Default;
        }

        for (var from = schemaVersion; from < SchemaVersion; from++)
        {
            _migrations[from - 1](document);
            document["schemaVersion"] = from + 1;
        }

        var manufacturerName = ReadString(document, "manufacturer");
        return new DeviceSettings(
            new ContactInfo(ReadString(document, "companyName"), ReadString(document, "mobileNumber")),
            OrderFormats.All.Select(format => format.Company).FirstOrDefault(company => company.Name == manufacturerName));
    }

    // A missing or null property is a missing value; a value of any other kind than a string is unreadable.
    private static string? ReadString(JsonObject document, string property) =>
        document[property] switch
        {
            null => null,
            JsonValue value when value.TryGetValue<string>(out var text) => text,
            _ => throw new FormatException($"The stored device settings' {property} is not a string."),
        };
}
