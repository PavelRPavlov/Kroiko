using FluentAssertions;
using Kroiko.Client.Blazor.Conversion;
using Kroiko.Client.Blazor.Updates;
using Kroiko.Client.Tests.Conversion;
using Kroiko.Domain.CellsExtracting;
using Kroiko.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using Xunit;

namespace Kroiko.Client.Tests.Updates;

/// <summary>
/// The offer of a new version: the reload snackbar, "По-късно", the unsaved-work guard and the manual check in About
/// (docs/implementation/06-updates-and-about.md, step 3; ADR-0002 §1–4). <see cref="AppUpdates"/> runs on the faked
/// <c>updates.js</c>; <c>E2E/UpdateOfferTests</c> drives the snackbar and About in the published app.
/// </summary>
public sealed class UpdateOfferTests : IAsyncDisposable
{
    private readonly FakeUpdatesRuntime _js = new();
    private readonly FakeConfirmation _confirmation = new();
    private readonly AppUpdates _updates;
    private readonly ConverterState _order;
    private readonly UpdateOffer _offer;

    public UpdateOfferTests()
    {
        _updates = new AppUpdates(_js, NullLogger<AppUpdates>.Instance);
        _order = new ConverterState(_confirmation, new FakeDeviceSettingsStore(), new FakeFileDownloader(),
            new FakeFolderPicker(), NullLogger<ConverterState>.Instance);
        _offer = new UpdateOffer(_updates, _order, _confirmation, NullLogger<UpdateOffer>.Instance);
    }

    public async ValueTask DisposeAsync()
    {
        _offer.Dispose();
        await _updates.DisposeAsync();
    }

    [Fact]
    public async Task Nothing_is_offered_until_a_new_version_waits()
    {
        await _updates.StartAsync();

        _offer.IsReady.Should().BeFalse();
        _offer.ShowsSnackbar.Should().BeFalse();
    }

    [Fact]
    public async Task A_waiting_new_version_shows_the_snackbar()
    {
        var changes = 0;
        _offer.Changed += () => changes++;
        await _updates.StartAsync();

        _js.Module.Notify("OnUpdateReady");

        _offer.IsReady.Should().BeTrue();
        _offer.ShowsSnackbar.Should().BeTrue();
        changes.Should().Be(1);
    }

    [Fact]
    public async Task Later_hides_the_snackbar_and_keeps_the_offer_for_About()
    {
        await ReadyAsync();
        var changes = 0;
        _offer.Changed += () => changes++;

        _offer.Postpone();

        _offer.ShowsSnackbar.Should().BeFalse();
        _offer.IsReady.Should().BeTrue();
        changes.Should().Be(1);
    }

    [Fact]
    public async Task Reload_without_unsaved_work_applies_the_new_version_without_asking()
    {
        await ReadyAsync();

        await _offer.ReloadAsync();

        _confirmation.Questions.Should().BeEmpty();
        _js.Module.Applied.Should().Be(1);
    }

    [Fact]
    public async Task Reload_over_unsaved_work_asks_first_and_no_keeps_the_Order_and_the_offer()
    {
        await ReadyAsync();
        await LoadOrderAsync();
        _confirmation.Answer = false;

        await _offer.ReloadAsync();

        _confirmation.Questions.Should().Equal(
            "Текущата поръчка ще бъде изгубена. Да презаредя ли с новата версия?");
        _js.Module.Applied.Should().Be(0);
        _order.IsFileLoaded.Should().BeTrue();
        _offer.ShowsSnackbar.Should().BeTrue();
        _offer.IsReloading.Should().BeFalse();
    }

    [Fact]
    public async Task Reload_over_unsaved_work_applies_the_new_version_after_yes()
    {
        await ReadyAsync();
        await LoadOrderAsync();
        _confirmation.Answer = true;

        await _offer.ReloadAsync();

        _confirmation.Questions.Should().ContainSingle();
        _js.Module.Applied.Should().Be(1);
    }

    [Fact]
    public async Task A_question_that_cannot_be_asked_applies_nothing_and_says_so()
    {
        await ReadyAsync();
        await LoadOrderAsync();
        _confirmation.Failure = new InvalidOperationException("The dialog could not open.");
        var errors = new List<string>();
        _offer.Error += errors.Add;

        await _offer.Invoking(offer => offer.ReloadAsync()).Should().NotThrowAsync();

        _js.Module.Applied.Should().Be(0);
        errors.Should().Equal("Действието не можа да бъде потвърдено.");
        _offer.ShowsSnackbar.Should().BeTrue();
    }

    [Fact]
    public async Task A_new_version_that_cannot_be_applied_says_so_and_keeps_the_offer()
    {
        await ReadyAsync();
        _js.Module.Failing = "applyUpdate";
        var errors = new List<string>();
        _offer.Error += errors.Add;

        await _offer.ReloadAsync();

        errors.Should().Equal("Новата версия не можа да бъде заредена. Тя ще се зареди при следващото отваряне на приложението.");
        _offer.ShowsSnackbar.Should().BeTrue();
        _offer.IsReloading.Should().BeFalse();
    }

    [Fact]
    public async Task Once_the_new_version_is_being_applied_reload_does_nothing_more()
    {
        await ReadyAsync();
        await _offer.ReloadAsync();

        _offer.IsReloading.Should().BeTrue();
        await _offer.ReloadAsync();

        _js.Module.Applied.Should().Be(1);
    }

    [Theory]
    [InlineData("upToDate", "Използвате най-новата версия.")]
    [InlineData("downloading", "Изтегля се нова версия. Ще можете да презаредите, щом е готова.")]
    [InlineData("offline", "Няма връзка със сървъра. Опитайте отново по-късно.")]
    public async Task The_manual_check_reports_what_it_found(string outcome, string report)
    {
        await _updates.StartAsync();
        _js.Module.CheckOutcome = outcome;

        (await _offer.CheckNowAsync()).Should().Be(report);
    }

    [Fact]
    public async Task The_whole_app_shares_one_AppUpdates_and_one_offer()
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddScoped<IJSRuntime>(_ => new FakeUpdatesRuntime())
            .AddScoped<IConfirmation, FakeConfirmation>()
            .AddScoped<IDeviceSettingsStore, FakeDeviceSettingsStore>()
            .AddScoped<IFileDownloader, FakeFileDownloader>()
            .AddScoped<IFolderPicker, FakeFolderPicker>()
            .AddConverterState()
            .AddAppUpdates();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        await using var app = provider.CreateAsyncScope();

        // The snackbar (MainLayout) and About see the same offer, so "По-късно" in one holds for the other.
        app.ServiceProvider.GetRequiredService<AppUpdates>().Should()
            .BeSameAs(app.ServiceProvider.GetRequiredService<AppUpdates>());
        app.ServiceProvider.GetRequiredService<UpdateOffer>().Should()
            .BeSameAs(app.ServiceProvider.GetRequiredService<UpdateOffer>());
    }

    private async Task LoadOrderAsync()
    {
        await _order.SelectManufacturerAsync(SupportedCompanies.Lonira);
        await using var polyboard = File.OpenRead(TestData.Polyboard("wardrobes-4-materials"));
        (await _order.UploadAsync(polyboard)).Should().BeTrue();
        _order.HasUnsavedWork.Should().BeTrue();
    }

    private async Task ReadyAsync()
    {
        await _updates.StartAsync();
        _js.Module.Notify("OnUpdateReady");
    }
}
