using MineDash.Models;

namespace MineDash.Services;

public interface ILogService
{
    /// <summary>
    /// Gets log entries from the last N minutes
    /// </summary>
    Task<List<LogEntry>> GetRecentLogsAsync(ServerConfig server, int minutes = 30, CancellationToken ct = default);

    /// <summary>
    /// Gets new log entries since the last read position
    /// </summary>
    Task<List<LogEntry>> GetNewLogsAsync(ServerConfig server, CancellationToken ct = default);

    /// <summary>
    /// Resets the read position for a server (used when toggling logs on)
    /// </summary>
    void ResetReadPosition(ServerConfig server);
}

public class LogEntry
{
    public DateTime Timestamp { get; set; }
    public string RawLine { get; set; } = string.Empty;
    public string Thread { get; set; } = string.Empty;
    public string Level { get; set; } = string.Empty;
}

