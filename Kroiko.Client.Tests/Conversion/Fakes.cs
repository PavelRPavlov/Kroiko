using Kroiko.Client.Blazor.Conversion;

namespace Kroiko.Client.Tests.Conversion;

/// <summary>Answers every question with <see cref="Answer"/> and records the questions asked.</summary>
internal sealed class FakeConfirmation : IConfirmation
{
    public bool Answer { get; set; } = true;

    public List<string> Questions { get; } = [];

    /// <summary>When set, asking throws it (the dialog could not open).</summary>
    public Exception? Failure { get; set; }

    public Task<bool> ConfirmAsync(string question)
    {
        Questions.Add(question);
        if (Failure is not null)
        {
            throw Failure;
        }

        return Task.FromResult(Answer);
    }
}

/// <summary>An uploaded file whose reading fails (the browser lost it, it is too large, …).</summary>
internal sealed class FailingStream : MemoryStream
{
    public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken) =>
        throw new IOException("The file could not be read.");

    public override int Read(byte[] buffer, int offset, int count) => throw new IOException("The file could not be read.");
}

/// <summary>Device settings held in memory: <see cref="Stored"/> is what the device remembers.</summary>
internal sealed class FakeDeviceSettingsStore : IDeviceSettingsStore
{
    public DeviceSettings Stored { get; set; } = DeviceSettings.Default;

    /// <summary>When set, saving throws it (storage full, blocked, …).</summary>
    public Exception? SaveFailure { get; set; }

    /// <summary>When set, loading throws it (storage blocked, …).</summary>
    public Exception? LoadFailure { get; set; }

    public Task<DeviceSettings> LoadAsync() => LoadFailure is null ? Task.FromResult(Stored) : throw LoadFailure;

    public Task SaveAsync(DeviceSettings settings)
    {
        if (SaveFailure is not null)
        {
            throw SaveFailure;
        }

        Stored = settings;
        return Task.CompletedTask;
    }
}

/// <summary>Browser storage held in memory: <see cref="Items"/> is what the browser holds.</summary>
internal sealed class FakeBrowserStorage : IBrowserStorage
{
    public Dictionary<string, string> Items { get; } = [];

    /// <summary>How many times something was written.</summary>
    public int Writes { get; private set; }

    /// <summary>When set, every call throws it (storage disabled, blocked, full, …).</summary>
    public Exception? Failure { get; set; }

    public ValueTask<string?> GetItemAsync(string key)
    {
        if (Failure is not null)
        {
            throw Failure;
        }

        return ValueTask.FromResult(Items.GetValueOrDefault(key));
    }

    public ValueTask SetItemAsync(string key, string value)
    {
        if (Failure is not null)
        {
            throw Failure;
        }

        Items[key] = value;
        Writes++;
        return ValueTask.CompletedTask;
    }
}

/// <summary>Browser downloads held in memory: <see cref="Downloads"/> are the files triggered, in order.</summary>
internal sealed class FakeFileDownloader : IFileDownloader
{
    public List<(string FileName, byte[] Content)> Downloads { get; } = [];

    /// <summary>When set, triggering the download with this zero-based index throws <see cref="Failure"/>.</summary>
    public int? FailAt { get; set; }

    public Exception Failure { get; set; } = new InvalidOperationException("The download could not be started.");

    /// <summary>Runs once each download is triggered, e.g. to check the busy flag or to edit the Order meanwhile.</summary>
    public Action? OnDownload { get; set; }

    public Task DownloadAsync(string fileName, byte[] content)
    {
        if (FailAt == Downloads.Count)
        {
            throw Failure;
        }

        Downloads.Add((fileName, content));
        OnDownload?.Invoke();
        return Task.CompletedTask;
    }
}

/// <summary>
/// A folder picker whose picked folder is held in memory: <see cref="Outcome"/> is how the next pick ends and
/// <see cref="Folder"/> is the folder it picks.
/// </summary>
internal sealed class FakeFolderPicker : IFolderPicker
{
    public FolderPickOutcome Outcome { get; set; } = FolderPickOutcome.Picked;

    public FakeFolder Folder { get; } = new();

    public bool Available { get; set; } = true;

    /// <summary>How many times the picker was opened.</summary>
    public int Picks { get; private set; }

    /// <summary>When set, opening the picker throws it (the interop failed).</summary>
    public Exception? Failure { get; set; }

    /// <summary>When set, the pick waits for it, as the real picker waits for the operator.</summary>
    public Task? PickedWhen { get; set; }

    public Task<bool> IsAvailableAsync() => Task.FromResult(Available);

    public async Task<FolderPick> PickAsync()
    {
        Picks++;
        if (PickedWhen is not null)
        {
            await PickedWhen;
        }

        if (Failure is not null)
        {
            throw Failure;
        }

        return Outcome switch
        {
            FolderPickOutcome.Picked => FolderPick.Picked(Folder),
            FolderPickOutcome.Cancelled => FolderPick.Cancelled,
            _ => FolderPick.Blocked,
        };
    }
}

/// <summary>A picked folder held in memory: <see cref="Names"/> is what it holds, <see cref="Writes"/> what was written, in order.</summary>
internal sealed class FakeFolder : IPickedFolder
{
    public string Name { get; set; } = "Поръчки";

    public List<string> Names { get; } = [];

    public List<(string FileName, byte[] Content)> Writes { get; } = [];

    /// <summary>When set, listing the folder throws it.</summary>
    public Exception? ListFailure { get; set; }

    /// <summary>When set, writing the file with this zero-based index throws <see cref="WriteFailure"/>.</summary>
    public int? FailWriteAt { get; set; }

    public Exception WriteFailure { get; set; } = new InvalidOperationException("The file could not be written.");

    /// <summary>Runs once each file is written, e.g. to check the busy flag or to edit the Order meanwhile.</summary>
    public Action? OnWrite { get; set; }

    public bool Disposed { get; private set; }

    public Task<IReadOnlyList<string>> ListNamesAsync() =>
        ListFailure is null ? Task.FromResult<IReadOnlyList<string>>([.. Names]) : throw ListFailure;

    public Task WriteFileAsync(string fileName, byte[] content)
    {
        if (FailWriteAt == Writes.Count)
        {
            throw WriteFailure;
        }

        Writes.Add((fileName, content));
        Names.Add(fileName);
        OnWrite?.Invoke();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}
