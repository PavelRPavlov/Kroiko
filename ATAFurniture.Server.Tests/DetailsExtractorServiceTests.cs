using FluentAssertions;
using Kroiko.Domain.CellsExtracting;
using Kroiko.Testing;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ATAFurniture.Server.Tests;

/// <summary>
/// The Server's adapter over <c>PolyboardParser</c> keeps today's policy (ADR-0004 §3, ADR-0007 §8):
/// any bad line → a log entry and no Details. Only files it rejected for their line endings now parse (ADR-0006 §1).
/// </summary>
public sealed class DetailsExtractorServiceTests
{
    private readonly RecordingLogger _logger = new();

    private async Task<List<Detail>> ExtractAsync(string fixture)
    {
        using var stream = new MemoryStream(await File.ReadAllBytesAsync(TestData.Polyboard(fixture)));
        return await new DetailsExtractorService(_logger).ExtractDetails(stream);
    }

    [Fact]
    public async Task A_file_with_a_bad_line_yields_no_details_and_logs_the_line()
    {
        // Line 10 of the fixture has 9 fields; the other 19 lines are good.
        var details = await ExtractAsync("bad-field-count");

        details.Should().BeEmpty();
        _logger.Entries.Should().ContainSingle()
            .Which.Should().Match<(LogLevel Level, string Message)>(e =>
                e.Level == LogLevel.Error && e.Message.Contains("line 10") && e.Message.Contains("FieldCount"));
    }

    [Fact]
    public async Task An_LF_only_file_now_yields_the_same_details_as_its_CRLF_original()
    {
        var expected = await ExtractAsync("wardrobes-4-materials");

        var details = await ExtractAsync("wardrobes-4-materials-lf");

        details.Should().NotBeEmpty().And.Equal(expected);
        _logger.Entries.Should().BeEmpty();
    }

    private sealed class RecordingLogger : ILogger<DetailsExtractorService>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Entries.Add((logLevel, formatter(state, exception)));
    }
}
