using MineDash.Models;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace MineDash.Services;

public class LogService : ILogService
{
    private readonly ILogger<LogService> _logger;

    public LogService(ILogger<LogService> logger)
    {
        _logger = logger;
    }

    public async Task<(List<LogEntry> Entries, long EndPosition)> GetRecentLogsAsync(
        ServerConfig server, int minutes = 30, CancellationToken ct = default)
    {
        var cutoffTime = DateTime.Now.AddMinutes(-minutes);
        var entries = new List<LogEntry>();
        long endPosition = 0;

        try
        {
            var logPath = server.LogPath?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(logPath))
            {
                _logger.LogDebug("No log path configured for server {ServerName}", server.Name);
                return (entries, endPosition);
            }

            _logger.LogDebug("Loading logs for {ServerName} from {LogPath}", server.Name, logPath);

            if (!await CanReadLogFileAsync(logPath))
                return (entries, endPosition);

            entries = await ReadLogFileAsync(logPath, cutoffTime, ct);
            endPosition = await GetFileLengthAsync(logPath, ct);

            _logger.LogInformation(
                "Loaded {Count} log entries for {ServerName} from {LogPath}",
                entries.Count, server.Name, logPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading logs for {ServerName} from {LogPath}",
                server.Name, server.LogPath?.Trim() ?? "");
        }

        return (entries, endPosition);
    }

    public async Task<(List<LogEntry> Entries, long EndPosition)> ReadLogsFromPositionAsync(
        ServerConfig server, long fromPosition, CancellationToken ct = default)
    {
        var entries = new List<LogEntry>();
        var endPosition = fromPosition;

        try
        {
            var logPath = server.LogPath?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(logPath))
                return (entries, endPosition);

            if (!await CanReadLogFileAsync(logPath))
                return (entries, endPosition);

            var fileLength = await GetFileLengthAsync(logPath, ct);
            if (fileLength <= fromPosition)
                return (entries, fromPosition);

            await using var fileStream = new FileStream(
                logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            fileStream.Position = fromPosition;

            using var reader = new StreamReader(fileStream);
            string? line;
            while ((line = await reader.ReadLineAsync(ct)) != null)
            {
                var entry = ParseLogLine(line);
                if (entry != null)
                    entries.Add(entry);
            }

            endPosition = fileStream.Position;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading new logs for {ServerName} from {LogPath}",
                server.Name, server.LogPath?.Trim() ?? "");
        }

        return (entries, endPosition);
    }

    private static async Task<bool> CanReadLogFileAsync(string logPath)
    {
        try
        {
            await using var testStream = new FileStream(
                logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return true;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch
        {
            return File.Exists(logPath);
        }
    }

    private static async Task<long> GetFileLengthAsync(string logPath, CancellationToken ct)
    {
        await using var fileStream = new FileStream(
            logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return fileStream.Length;
    }

    private async Task<List<LogEntry>> ReadLogFileAsync(string logPath, DateTime since, CancellationToken ct)
    {
        var entries = new List<LogEntry>();

        if (!File.Exists(logPath))
            return entries;

        try
        {
            await using var fileStream = new FileStream(
                logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fileStream);

            string? line;
            while ((line = await reader.ReadLineAsync(ct)) != null)
            {
                ct.ThrowIfCancellationRequested();

                var entry = ParseLogLine(line);
                if (entry != null && entry.Timestamp >= since)
                    entries.Add(entry);
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Permission denied reading log file {LogPath}", logPath);
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "IO error reading log file {LogPath}", logPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading log file {LogPath}", logPath);
        }

        return entries;
    }

    private LogEntry? ParseLogLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;

        var match = Regex.Match(line, @"\[(\d{2}):(\d{2}):(\d{2})\]\s+\[(.*?)/(.*?)\]:");

        if (match.Success)
        {
            var hour = int.Parse(match.Groups[1].Value);
            var minute = int.Parse(match.Groups[2].Value);
            var second = int.Parse(match.Groups[3].Value);
            var thread = match.Groups[4].Value.Trim();
            var level = match.Groups[5].Value.Trim();

            var today = DateTime.Now.Date;
            var timestamp = new DateTime(today.Year, today.Month, today.Day, hour, minute, second);

            if (timestamp > DateTime.Now)
                timestamp = timestamp.AddDays(-1);

            return new LogEntry
            {
                Timestamp = timestamp,
                RawLine = line,
                Thread = thread,
                Level = level
            };
        }

        return new LogEntry
        {
            Timestamp = DateTime.Now,
            RawLine = line,
            Thread = string.Empty,
            Level = string.Empty
        };
    }
}
