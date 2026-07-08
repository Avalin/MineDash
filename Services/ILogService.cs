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

    /// <summary>
    /// Checks whether MineDash can find latest.log under the configured server folder.
    /// </summary>
    Task<LogPathDiagnostics> DiagnoseLogAccessAsync(ServerConfig server, CancellationToken ct = default);
}

public sealed class LogPathDiagnostics
{
    public string ConfiguredPath { get; init; } = string.Empty;
    public string? ResolvedPath { get; init; }
    public bool Readable { get; init; }
    public long FileSizeBytes { get; init; }
    public string? LastLinePreview { get; init; }
    public IReadOnlyList<string> TriedPaths { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> MountHints { get; init; } = Array.Empty<string>();
    public string Summary { get; init; } = string.Empty;
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

