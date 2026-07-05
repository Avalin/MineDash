using MineDash.Models;

namespace MineDash.Services;

public interface ILogService
{
    /// <summary>
    /// Gets log entries from the last N minutes and the file position after reading.
    /// </summary>
    Task<(List<LogEntry> Entries, long EndPosition)> GetRecentLogsAsync(
        ServerConfig server, int minutes = 30, CancellationToken ct = default);

    /// <summary>
    /// Reads new log lines starting at <paramref name="fromPosition"/>.
    /// </summary>
    Task<(List<LogEntry> Entries, long EndPosition)> ReadLogsFromPositionAsync(
        ServerConfig server, long fromPosition, CancellationToken ct = default);
}

public class LogEntry
{
    public DateTime Timestamp { get; set; }
    public bool HasParsedTimestamp { get; set; }
    public string RawLine { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Thread { get; set; } = string.Empty;
    public string Level { get; set; } = string.Empty;
}

