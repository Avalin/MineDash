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
        var cutoffUtc = DateTime.UtcNow.AddMinutes(-minutes);
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

            if (!await CanReadLogFileAsync(logPath))
                return (entries, endPosition);

            entries = await ReadLogFileAsync(server, logPath, cutoffUtc, ct);
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

            // latest.log was rotated or truncated
            if (fileLength < fromPosition)
                fromPosition = 0;

            if (fileLength <= fromPosition)
                return (entries, fromPosition);

            await using var fileStream = new FileStream(
                logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            fileStream.Position = fromPosition;

            using var reader = new StreamReader(fileStream);
            DateTime? lastTimestampUtc = null;
            string? line;
            while ((line = await reader.ReadLineAsync(ct)) != null)
            {
                var entry = ParseLogLine(line, server, lastTimestampUtc);
                if (entry is null)
                    continue;

                if (entry.HasParsedTimestamp)
                    lastTimestampUtc = entry.Timestamp;

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

    private async Task<List<LogEntry>> ReadLogFileAsync(
        ServerConfig server, string logPath, DateTime cutoffUtc, CancellationToken ct)
    {
        var entries = new List<LogEntry>();

        if (!File.Exists(logPath))
            return entries;

        try
        {
            await using var fileStream = new FileStream(
                logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fileStream);

            DateTime? lastTimestampUtc = null;
            string? line;
            while ((line = await reader.ReadLineAsync(ct)) != null)
            {
                ct.ThrowIfCancellationRequested();

                var entry = ParseLogLine(line, server, lastTimestampUtc);
                if (entry is null)
                    continue;

                if (entry.HasParsedTimestamp)
                    lastTimestampUtc = entry.Timestamp;

                if (entry.Timestamp >= cutoffUtc)
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

    private static LogEntry? ParseLogLine(string line, ServerConfig server, DateTime? fallbackUtc)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;

        // [HH:mm:ss] [HH:mm:ss] [thread/LEVEL]: message
        var match = Regex.Match(line,
            @"^\[(\d{2}):(\d{2}):(\d{2})\](?:\s+\[(\d{2}):(\d{2}):(\d{2})\])?\s+\[(.*?)/(.*?)\]:\s*(.*)$");

        if (match.Success)
        {
            var hour = int.Parse(match.Groups[1].Value);
            var minute = int.Parse(match.Groups[2].Value);
            var second = int.Parse(match.Groups[3].Value);
            var thread = match.Groups[7].Value.Trim();
            var level = match.Groups[8].Value.Trim();
            var message = match.Groups[9].Value;

            return new LogEntry
            {
                Timestamp = BuildLogTimestampUtc(hour, minute, second, server),
                HasParsedTimestamp = true,
                RawLine = line,
                Message = message,
                Thread = thread,
                Level = level
            };
        }

        if (fallbackUtc is null)
            return null;

        // Stack traces and wrapped lines inherit the previous log line's time
        return new LogEntry
        {
            Timestamp = fallbackUtc.Value,
            HasParsedTimestamp = false,
            RawLine = line,
            Message = line.Trim(),
            Thread = string.Empty,
            Level = string.Empty
        };
    }

    private static DateTime BuildLogTimestampUtc(int hour, int minute, int second, ServerConfig server)
    {
        var tzId = string.IsNullOrWhiteSpace(server.LogTimeZoneId) ? "UTC" : server.LogTimeZoneId;
        var tz = TimeDisplayService.ResolveTimeZone(tzId);

        var nowInLogTz = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
        var date = nowInLogTz.Date;
        var localTime = new DateTime(date.Year, date.Month, date.Day, hour, minute, second);

        if (localTime > nowInLogTz)
            localTime = localTime.AddDays(-1);

        return TimeZoneInfo.ConvertTimeToUtc(localTime, tz);
    }
}
