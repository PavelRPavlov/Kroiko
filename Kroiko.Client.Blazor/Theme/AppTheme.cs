using MudBlazor;

namespace Kroiko.Client.Blazor.Theme;

/// <summary>
/// The app's MudBlazor theme: MudBlazor's default light and dark palettes (ADR-0014 §4). Rebranding the palettes
/// happens here.
/// </summary>
public static class AppTheme
{
    public static MudTheme Theme { get; } = new()
    {
        PaletteLight = new PaletteLight(),
        PaletteDark = new PaletteDark(),
    };
}
