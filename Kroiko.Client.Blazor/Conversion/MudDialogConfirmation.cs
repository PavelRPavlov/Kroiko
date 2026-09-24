using MudBlazor;

namespace Kroiko.Client.Blazor.Conversion;

/// <summary>
/// The app's <see cref="IConfirmation"/>: a Bulgarian MudBlazor message-box dialog with "Да" and "Не"
/// (ADR-0005 §5). Closing it any other way (Escape) counts as "Не".
/// </summary>
public sealed class MudDialogConfirmation(IDialogService dialogs) : IConfirmation
{
    private const string Title = "Потвърждение";
    private const string Yes = "Да";
    private const string No = "Не";

    public async Task<bool> ConfirmAsync(string question) =>
        await dialogs.ShowMessageBoxAsync(Title, question, yesText: Yes, noText: No) == true;
}
