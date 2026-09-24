using Kroiko.Client.Blazor.Conversion;
using Microsoft.Extensions.Logging;

namespace Kroiko.Client.Blazor.Updates;

/// <summary>
/// What the operator is offered once a new version waits (ADR-0002 §1–4), over <see cref="AppUpdates"/>: the reload
/// snackbar until they choose "По-късно", the same offer in the About dialog, the unsaved-work guard of "Презареди" and
/// the manual check in About. <c>MainLayout</c> shows the snackbar and About reads it, both re-rendering on
/// <see cref="Changed"/>. Nothing here throws: a failure is logged and raises <see cref="Error"/> in Bulgarian.
/// </summary>
public sealed class UpdateOffer : IDisposable
{
    internal const string ReadyMessage = "Нова версия е налична";
    internal const string LoseOrderQuestion = "Текущата поръчка ще бъде изгубена. Да презаредя ли с новата версия?";
    internal const string ApplyFailedMessage =
        "Новата версия не можа да бъде заредена. Тя ще се зареди при следващото отваряне на приложението.";
    internal const string UpToDateReport = "Използвате най-новата версия.";
    internal const string DownloadingReport = "Изтегля се нова версия. Ще можете да презаредите, щом е готова.";
    internal const string OfflineReport = "Няма връзка със сървъра. Опитайте отново по-късно.";

    private readonly AppUpdates _updates;
    private readonly ConverterState _order;
    private readonly IConfirmation _confirmation;
    private readonly ILogger<UpdateOffer> _logger;
    private bool _postponed;

    public UpdateOffer(AppUpdates updates, ConverterState order, IConfirmation confirmation, ILogger<UpdateOffer> logger)
    {
        _updates = updates;
        _order = order;
        _confirmation = confirmation;
        _logger = logger;
        _updates.UpdateReady += OnUpdateReady;
    }

    /// <summary>
    /// Something about the offer changed; re-render. Also raised from a JS callback, which WebAssembly runs on its one
    /// thread, so a component can re-render directly (as <c>ConverterStateComponentBase</c> does).
    /// </summary>
    public event Action? Changed;

    /// <summary>"Презареди" failed; show the Bulgarian message in a snackbar.</summary>
    public event Action<string>? Error;

    /// <summary>A new version waits to be applied: About offers it, whether or not the snackbar still does.</summary>
    public bool IsReady => _updates.IsUpdateReady;

    /// <summary>The reload snackbar is shown: a new version waits and the operator has not chosen "По-късно".</summary>
    public bool ShowsSnackbar => IsReady && !_postponed;

    /// <summary>
    /// "Презареди" is under way: asking, or applying the new version until the page reloads. The reload buttons are
    /// disabled meanwhile.
    /// </summary>
    public bool IsReloading { get; private set; }

    /// <summary>
    /// "По-късно": hides the snackbar for the rest of the session (ADR-0002 §3). About keeps the offer, and the next
    /// launch applies the new version anyway.
    /// </summary>
    public void Postpone()
    {
        _postponed = true;
        Changed?.Invoke();
    }

    /// <summary>
    /// "Презареди": applies the new version, which reloads the page (ADR-0002 §1). Over unsaved work it asks first, and
    /// "Не" keeps the Order and the offer (ADR-0002 §2).
    /// </summary>
    public async Task ReloadAsync()
    {
        if (IsReloading)
            return;

        SetReloading(true);
        if (_order.HasUnsavedWork && !await ConfirmLosingTheOrderAsync())
        {
            SetReloading(false);
            return;
        }

        // Applied, the page reloads; the buttons stay disabled until it does.
        if (!await _updates.ApplyUpdateAsync())
        {
            Error?.Invoke(ApplyFailedMessage);
            SetReloading(false);
        }
    }

    /// <summary>
    /// "Провери за обновления": asks the server for a newer version now and reports what it found in Bulgarian
    /// (ADR-0002 §4). A version that already waits is <see cref="IsReady"/>, which About shows instead of the check.
    /// </summary>
    public async Task<string> CheckNowAsync() =>
        await _updates.CheckNowAsync() switch
        {
            UpdateCheck.UpToDate => UpToDateReport,
            UpdateCheck.Downloading => DownloadingReport,
            _ => OfflineReport,
        };

    public void Dispose() => _updates.UpdateReady -= OnUpdateReady;

    private async Task<bool> ConfirmLosingTheOrderAsync()
    {
        try
        {
            return await _confirmation.ConfirmAsync(LoseOrderQuestion);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not ask before reloading over unsaved work.");
            Error?.Invoke(ConverterState.ConfirmFailedMessage);
            return false;
        }
    }

    private void SetReloading(bool reloading)
    {
        IsReloading = reloading;
        Changed?.Invoke();
    }

    private void OnUpdateReady() => Changed?.Invoke();
}
