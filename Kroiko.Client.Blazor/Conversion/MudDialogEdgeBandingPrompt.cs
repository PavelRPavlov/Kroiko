using Kroiko.Client.Blazor.Components;
using Kroiko.Domain;
using Kroiko.Domain.TemplateBuilding;
using MudBlazor;

namespace Kroiko.Client.Blazor.Conversion;

/// <summary>
/// The app's <see cref="IEdgeBandingPrompt"/>: <see cref="EdgeBandingDialog"/>, a MudBlazor dialog with a width and a
/// thickness picker per material (ADR-0015). "Отказ", Escape and the close button all cancel.
/// </summary>
public sealed class MudDialogEdgeBandingPrompt(IDialogService dialogs) : IEdgeBandingPrompt
{
    private const string Title = "Кантове без ширина или дебелина";

    public async Task<IReadOnlyDictionary<string, MegaTradingEdge>?> AskAsync(MissingEdgeBanding missing)
    {
        ArgumentNullException.ThrowIfNull(missing);

        var parameters = new DialogParameters<EdgeBandingDialog> { { d => d.Missing, missing } };
        var options = new DialogOptions { CloseButton = true, MaxWidth = MaxWidth.Small, FullWidth = true };
        var dialog = await dialogs.ShowAsync<EdgeBandingDialog>(Title, parameters, options);
        var result = await dialog.Result;
        return result is { Canceled: false, Data: IReadOnlyDictionary<string, MegaTradingEdge> picks } ? picks : null;
    }
}
