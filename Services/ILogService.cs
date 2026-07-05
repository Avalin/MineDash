using MineDash.Models;

namespace MineDash.Services;

public interface ILogService
{
    /// <summary>
    /// Reads the last N lines from the log file and returns the current file position.
    /// </summary>
    Task<(List<LogEntry> Entries, long EndPosition)> GetRecentLogsAsync(
        ServerConfig server, int tailLines = 500, CancellationToken ct = default);

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
    public int Sequence { get; set; }
    public string RawLine { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Thread { get; set; } = string.Empty;
    public string Level { get; set; } = string.Empty;
}

