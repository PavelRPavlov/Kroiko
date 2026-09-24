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
