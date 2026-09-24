using Kroiko.Domain;
using Kroiko.Domain.CellsExtracting;

namespace Kroiko.Client.Blazor.Conversion;

/// <summary>
/// What this device remembers between visits: the last contact info and manufacturer used for a
/// successful generation (ADR-0005 §6). Nothing else of an Order is persisted (ADR-0005 §4).
/// </summary>
/// <param name="Contact">The end customer's contacts; both parts may be missing.</param>
/// <param name="Manufacturer">The last manufacturer, or <c>null</c> when none was used yet.</param>
public sealed record DeviceSettings(ContactInfo Contact, SupportedCompany? Manufacturer)
{
    /// <summary>A device that remembers nothing yet.</summary>
    public static DeviceSettings Default { get; } = new(new ContactInfo(null, null), null);
}

/// <summary>
/// Loads and saves the <see cref="DeviceSettings"/>. The app keeps them in browser storage; the tests fake
/// it (ADR-0007 §4).
/// </summary>
public interface IDeviceSettingsStore
{
    /// <summary>The remembered settings, or <see cref="DeviceSettings.Default"/> when there are none.</summary>
    Task<DeviceSettings> LoadAsync();

    /// <summary>Remembers <paramref name="settings"/> on this device.</summary>
    Task SaveAsync(DeviceSettings settings);
}
