using System.Reflection;
using FluentAssertions;
using Kroiko.Client.Blazor.Updates;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using Xunit;

namespace Kroiko.Client.Tests.Updates;

/// <summary>
/// The app learns about a new version from <c>wwwroot/js/updates.js</c>, applies it and checks for one on demand
/// (docs/implementation/06-updates-and-about.md, step 2; ADR-0002 §1, §3–4). The JS runtime is faked at the module;
/// <c>E2E/UpdatesModuleTests</c> covers the real module in the published app.
/// </summary>
public sealed class AppUpdatesTests : IAsyncDisposable
{
    private readonly FakeUpdatesRuntime _js = new();
    private readonly AppUpdates _updates;

    public AppUpdatesTests() => _updates = new AppUpdates(_js, NullLogger<AppUpdates>.Instance);

    public ValueTask DisposeAsync() => _updates.DisposeAsync();

    [Fact]
    public async Task Starting_subscribes_to_the_updates_module()
    {
        await _updates.StartAsync();

        _js.Imports.Should().Equal("./js/updates.js");
        _js.Module.Subscriber.Should().NotBeNull();
        _updates.IsUpdateReady.Should().BeFalse();
    }

    [Fact]
    public async Task A_waiting_new_version_is_ready_and_says_so()
    {
        var raised = 0;
        _updates.UpdateReady += () => raised++;
        await _updates.StartAsync();

        _js.Module.Notify("OnUpdateReady");

        _updates.IsUpdateReady.Should().BeTrue();
        raised.Should().Be(1);
    }

    [Fact]
    public async Task A_version_already_ready_is_not_announced_again()
    {
        var raised = 0;
        _updates.UpdateReady += () => raised++;
        await _updates.StartAsync();

        // A newer version can replace the waiting one; the offer stays the same (ADR-0002 §3: "По-късно" holds).
        _js.Module.Notify("OnUpdateReady");
        _js.Module.Notify("OnUpdateReady");

        raised.Should().Be(1);
    }

    [Fact]
    public async Task Starting_again_does_not_subscribe_again()
    {
        await _updates.StartAsync();
        await _updates.StartAsync();

        _js.Imports.Should().ContainSingle();
        _js.Module.Subscriptions.Should().Be(1);
    }

    [Fact]
    public async Task A_start_that_fails_does_not_stop_the_app()
    {
        _js.ImportFailure = new JSException("The module could not be loaded.");

        await _updates.Invoking(updates => updates.StartAsync()).Should().NotThrowAsync();

        _updates.IsUpdateReady.Should().BeFalse();
    }

    [Fact]
    public async Task A_start_that_failed_can_be_tried_again()
    {
        _js.ImportFailure = new JSException("The module could not be loaded.");
        await _updates.StartAsync();

        _js.ImportFailure = null;
        await _updates.StartAsync();

        _js.Module.Subscriptions.Should().Be(1);
    }

    [Fact]
    public async Task A_subscription_that_fails_does_not_stop_the_app()
    {
        _js.Module.Failing = "subscribe";

        await _updates.Invoking(updates => updates.StartAsync()).Should().NotThrowAsync();
    }

    [Theory]
    [InlineData("upToDate", UpdateCheck.UpToDate)]
    [InlineData("downloading", UpdateCheck.Downloading)]
    [InlineData("offline", UpdateCheck.Offline)]
    public async Task Checking_now_reports_what_the_browser_found(string outcome, UpdateCheck reported)
    {
        _js.Module.CheckOutcome = outcome;

        (await _updates.CheckNowAsync()).Should().Be(reported);
    }

    [Fact]
    public async Task A_check_that_fails_reports_offline()
    {
        _js.Module.Failing = "checkNow";

        (await _updates.CheckNowAsync()).Should().Be(UpdateCheck.Offline);
    }

    [Fact]
    public async Task Applying_hands_the_waiting_version_over_to_the_module()
    {
        await _updates.StartAsync();
        _js.Module.Notify("OnUpdateReady");

        (await _updates.ApplyUpdateAsync()).Should().BeTrue();

        _js.Module.Applied.Should().Be(1);
    }

    [Fact]
    public async Task An_apply_that_fails_says_so()
    {
        _js.Module.Failing = "applyUpdate";

        (await _updates.ApplyUpdateAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Disposing_stops_listening_to_the_module()
    {
        await _updates.StartAsync();

        await _updates.DisposeAsync();

        _js.Module.Unsubscriptions.Should().Be(1);
        _js.Module.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task The_whole_app_shares_one_AppUpdates()
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddScoped<IJSRuntime>(_ => new FakeUpdatesRuntime())
            .AddAppUpdates();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        await using var app = provider.CreateAsyncScope();

        app.ServiceProvider.GetRequiredService<AppUpdates>().Should()
            .BeSameAs(app.ServiceProvider.GetRequiredService<AppUpdates>());
    }

    /// <summary>A JS runtime that can only import <c>updates.js</c>.</summary>
    private sealed class FakeUpdatesRuntime : IJSRuntime
    {
        public List<string> Imports { get; } = [];

        public FakeUpdatesModule Module { get; } = new();

        public Exception? ImportFailure { get; set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            if (identifier != "import")
                throw new JSException($"Could not find '{identifier}'.");

            Imports.Add((string)args![0]!);
            if (ImportFailure is not null)
                throw ImportFailure;
            return ValueTask.FromResult((TValue)(object)Module);
        }
    }

    /// <summary>
    /// <c>updates.js</c>: keeps the subscriber, which it calls back as the browser would, answers <c>checkNow</c> with
    /// <see cref="CheckOutcome"/> and records <c>applyUpdate</c>.
    /// </summary>
    private sealed class FakeUpdatesModule : IJSObjectReference
    {
        public object? Subscriber { get; private set; }

        public int Subscriptions { get; private set; }

        public int Unsubscriptions { get; private set; }

        public int Applied { get; private set; }

        public string CheckOutcome { get; set; } = "upToDate";

        /// <summary>A function name whose call throws, as the browser's interop would.</summary>
        public string? Failing { get; set; }

        public bool Disposed { get; private set; }

        /// <summary>Calls the subscriber's <c>[JSInvokable]</c> method by its name, as <c>invokeMethodAsync</c> does.</summary>
        public void Notify(string method)
        {
            var target = Subscriber!.GetType().GetProperty("Value")!.GetValue(Subscriber)!;
            var invokable = target.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Single(m => m.GetCustomAttribute<JSInvokableAttribute>() is { } attribute
                             && (attribute.Identifier ?? m.Name) == method);
            invokable.Invoke(target, null);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            if (identifier == Failing)
                throw new JSException($"'{identifier}' failed.");

            switch (identifier)
            {
                case "subscribe":
                    Subscriber = args![0];
                    Subscriptions++;
                    return ValueTask.FromResult(default(TValue)!);
                case "unsubscribe":
                    Unsubscriptions++;
                    return ValueTask.FromResult(default(TValue)!);
                case "applyUpdate":
                    Applied++;
                    return ValueTask.FromResult(default(TValue)!);
                case "checkNow":
                    return ValueTask.FromResult((TValue)(object)CheckOutcome);
                default:
                    throw new JSException($"Could not find '{identifier}'.");
            }
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
