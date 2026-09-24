namespace Kroiko.Client.Blazor.Conversion;

/// <summary>
/// Asks the operator a yes/no question before <see cref="ConverterState"/> discards their work
/// (ADR-0005 §5). The app answers it with a Bulgarian dialog; the tests fake it (ADR-0007 §4).
/// </summary>
public interface IConfirmation
{
    /// <summary>Shows <paramref name="question"/>; <c>true</c> when the operator says yes.</summary>
    Task<bool> ConfirmAsync(string question);
}
