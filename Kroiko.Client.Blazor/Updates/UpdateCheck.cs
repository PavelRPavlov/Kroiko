namespace Kroiko.Client.Blazor.Updates;

/// <summary>What a manual "check for updates" found (ADR-0002 §4).</summary>
public enum UpdateCheck
{
    /// <summary>The server has no newer version than the one running or waiting.</summary>
    UpToDate,

    /// <summary>A newer version is being downloaded, or is already waiting to be applied.</summary>
    Downloading,

    /// <summary>The server could not be reached, so nothing is known.</summary>
    Offline,
}
