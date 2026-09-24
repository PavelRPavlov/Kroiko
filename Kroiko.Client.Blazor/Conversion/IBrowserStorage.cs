using Microsoft.JSInterop;

namespace Kroiko.Client.Blazor.Conversion;

/// <summary>
/// The browser's key/value storage for this origin. The only interop of the device settings; everything
/// above it is plain C# the tests exercise with a fake.
/// </summary>
public interface IBrowserStorage
{
    /// <summary>The value stored under <paramref name="key"/>, or <c>null</c> when there is none.</summary>
    ValueTask<string?> GetItemAsync(string key);

    /// <summary>Stores <paramref name="value"/> under <paramref name="key"/>, replacing what was there.</summary>
    ValueTask SetItemAsync(string key, string value);
}

/// <summary>
/// <see cref="IBrowserStorage"/> over <c>window.localStorage</c>. It throws what the browser throws: storage can
/// be disabled, blocked by privacy settings or full.
/// </summary>
public sealed class BrowserLocalStorage(IJSRuntime js) : IBrowserStorage
{
    public ValueTask<string?> GetItemAsync(string key) => js.InvokeAsync<string?>("localStorage.getItem", key);

    public ValueTask SetItemAsync(string key, string value) => js.InvokeVoidAsync("localStorage.setItem", key, value);
}
