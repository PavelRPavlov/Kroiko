namespace Kroiko.Domain;

/// <summary>
/// A reason an order format refuses to generate, found by <see cref="IOrderFormat.Check"/> (ADR-0006 §3). The
/// PWA blocks generation while any exists and writes the Bulgarian text; the Server does not check.
/// </summary>
public abstract record OrderProblem;

/// <summary>
/// The files use more distinct materials than the format can list: MegaTrading's <c>.cut_mt</c> header has
/// room for <paramref name="Max"/>. <paramref name="Materials"/> are all of them, in order of first use.
/// </summary>
public sealed record TooManyMaterials(int Max, IReadOnlyList<string> Materials) : OrderProblem;
