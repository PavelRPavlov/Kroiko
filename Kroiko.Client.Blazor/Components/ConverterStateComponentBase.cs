using Kroiko.Client.Blazor.Conversion;
using Microsoft.AspNetCore.Components;

namespace Kroiko.Client.Blazor.Components;

/// <summary>
/// A component that reads the app's <see cref="Conversion.ConverterState"/> and re-renders on its one
/// <see cref="ConverterState.Changed"/> event (ADR-0005 §4). A derived component that overrides
/// <see cref="OnInitialized"/> or <see cref="Dispose"/> calls the base.
/// </summary>
public abstract class ConverterStateComponentBase : ComponentBase, IDisposable
{
    [Inject] protected ConverterState ConverterState { get; set; } = default!;

    protected override void OnInitialized() => ConverterState.Changed += OnConverterStateChanged;

    public virtual void Dispose() => ConverterState.Changed -= OnConverterStateChanged;

    // WebAssembly runs on one thread, so the state changes on the renderer's dispatcher and can re-render directly.
    private void OnConverterStateChanged() => StateHasChanged();
}
