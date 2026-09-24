namespace Kroiko.Client.Blazor.Conversion;

/// <summary>
/// A folder save that succeeded (<see cref="ConverterState.FolderSave"/>): the folder's name and the names the files
/// were written under, in order, for the confirmation (ADR-0003 §4).
/// </summary>
public sealed record FolderSave(string FolderName, IReadOnlyList<string> FileNames);
