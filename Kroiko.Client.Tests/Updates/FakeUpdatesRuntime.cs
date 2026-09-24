using System.Reflection;
using Microsoft.JSInterop;

namespace Kroiko.Client.Tests.Updates;

/// <summary>A JS runtime that can only import <c>updates.js</c>.</summary>
internal sealed class FakeUpdatesRuntime : IJSRuntime
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
internal sealed class FakeUpdatesModule : IJSObjectReference
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
