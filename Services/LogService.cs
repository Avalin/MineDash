using System.Globalization;
using MineDash.Models;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace MineDash.Services;

public class LogService : ILogService
{
    private const int TailReadBytes = 512 * 1024;

    private static readonly Regex DatedLogRegex = new(
        @"^\[(?<day>\d{2})(?<mon>[A-Za-z]{3})(?<year>\d{4})\s+(?<hour>\d{2}):(?<min>\d{2}):(?<sec>\d{2})(?:\.(?<ms>\d{1,3}))?\](?:\s+\[\d{2}:\d{2}:\d{2}\]){0,2}\s+\[(?<thread>[^/]+)/(?<level>[^\]]+)\]:\s*(?<msg>.*)$",
        RegexOptions.Compiled);

    private static readonly Regex TimeOnlyLogRegex = new(
        @"^\[(?<hour>\d{2}):(?<min>\d{2}):(?<sec>\d{2})\](?:\s+\[(?<hour2>\d{2}):(?<min2>\d{2}):(?<sec2>\d{2})\])?\s+\[(?<thread>[^/]+)/(?<level>[^\]]+)\]:\s*(?<msg>.*)$",
        RegexOptions.Compiled);

    private readonly ILogger<LogService> _logger;

    public LogService(ILogger<LogService> logger)
    {
        _logger = logger;
    }

    public async Task<(List<LogEntry> Entries, long EndPosition)> GetRecentLogsAsync(
        ServerConfig server, int tailLines = 500, CancellationToken ct = default)
    {
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
            {
                _logger.LogWarning("Cannot read log file for {ServerName} at {LogPath}", server.Name, logPath);
                return (entries, endPosition);
            }

            entries = await ReadLogFileTailAsync(server, logPath, tailLines, ct);
            endPosition = await GetFileLengthAsync(logPath, ct);

            _logger.LogInformation(
                "Loaded {Count} tail log entries for {ServerName} from {LogPath}",
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

            if (fileLength < fromPosition)
                fromPosition = 0;

            if (fileLength <= fromPosition)
                return (entries, fromPosition);

            await using var fileStream = new FileStream(
                logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            fileStream.Position = fromPosition;

            using var reader = new StreamReader(fileStream);
            entries = ReadLines(server, () => reader.ReadLine(), ct, out _);

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

    private static async Task<List<LogEntry>> ReadLogFileTailAsync(
        ServerConfig server, string logPath, int tailLines, CancellationToken ct)
    {
        if (!File.Exists(logPath))
            return new List<LogEntry>();

        await using var fileStream = new FileStream(
            logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        var start = Math.Max(0, fileStream.Length - TailReadBytes);
        fileStream.Seek(start, SeekOrigin.Begin);

        using var reader = new StreamReader(fileStream);
        if (start > 0)
            await reader.ReadLineAsync(ct);

        var entries = ReadLines(server, () => reader.ReadLine(), ct, out _);

        if (entries.Count <= tailLines)
            return entries;

        return entries.Skip(entries.Count - tailLines).ToList();
    }

    private static List<LogEntry> ReadLines(
        ServerConfig server,
        Func<string?> readLine,
        CancellationToken ct,
        out DateTime? lastTimestampUtc)
    {
        var entries = new List<LogEntry>();
        lastTimestampUtc = null;
        var sequence = 0;

        string? line;
        while ((line = readLine()) != null)
        {
            ct.ThrowIfCancellationRequested();

            var entry = ParseLogLine(line, server, lastTimestampUtc, sequence);
            if (entry is null)
                continue;

            if (entry.HasParsedTimestamp)
                lastTimestampUtc = entry.Timestamp;

            entries.Add(entry);
            sequence++;
        }

        return entries;
    }

    private static LogEntry? ParseLogLine(
        string line, ServerConfig server, DateTime? fallbackUtc, int sequence)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;

        var dated = DatedLogRegex.Match(line);
        if (dated.Success)
        {
            var timestamp = BuildLogTimestampUtc(
                int.Parse(dated.Groups["year"].Value),
                ParseMonth(dated.Groups["mon"].Value),
                int.Parse(dated.Groups["day"].Value),
                int.Parse(dated.Groups["hour"].Value),
                int.Parse(dated.Groups["min"].Value),
                int.Parse(dated.Groups["sec"].Value),
                ParseMilliseconds(dated.Groups["ms"].Value),
                server);

            return new LogEntry
            {
                Timestamp = timestamp,
                HasParsedTimestamp = true,
                Sequence = sequence,
                RawLine = line,
                Message = dated.Groups["msg"].Value,
                Thread = dated.Groups["thread"].Value.Trim(),
                Level = dated.Groups["level"].Value.Trim()
            };
        }

        var timeOnly = TimeOnlyLogRegex.Match(line);
        if (timeOnly.Success)
        {
            var timestamp = BuildLogTimestampUtc(
                DateTime.UtcNow.Year,
                DateTime.UtcNow.Month,
                DateTime.UtcNow.Day,
                int.Parse(timeOnly.Groups["hour"].Value),
                int.Parse(timeOnly.Groups["min"].Value),
                int.Parse(timeOnly.Groups["sec"].Value),
                0,
                server,
                inferDate: true);

            return new LogEntry
            {
                Timestamp = timestamp,
                HasParsedTimestamp = true,
                Sequence = sequence,
                RawLine = line,
                Message = timeOnly.Groups["msg"].Value,
                Thread = timeOnly.Groups["thread"].Value.Trim(),
                Level = timeOnly.Groups["level"].Value.Trim()
            };
        }

        if (fallbackUtc is null)
            return null;

        return new LogEntry
        {
            Timestamp = fallbackUtc.Value,
            HasParsedTimestamp = false,
            Sequence = sequence,
            RawLine = line,
            Message = line.Trim(),
            Thread = string.Empty,
            Level = string.Empty
        };
    }

    private static int ParseMonth(string month) =>
        DateTime.ParseExact(month, "MMM", CultureInfo.InvariantCulture).Month;

    private static int ParseMilliseconds(string value) =>
        string.IsNullOrEmpty(value) ? 0 : int.Parse(value.PadRight(3, '0'));

    private static DateTime BuildLogTimestampUtc(
        int year, int month, int day, int hour, int minute, int second, int millisecond,
        ServerConfig server, bool inferDate = false)
    {
        var tzId = string.IsNullOrWhiteSpace(server.LogTimeZoneId) ? "UTC" : server.LogTimeZoneId;
        var tz = TimeDisplayService.ResolveTimeZone(tzId);

        if (inferDate)
        {
            var nowInLogTz = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
            var date = nowInLogTz.Date;
            year = date.Year;
            month = date.Month;
            day = date.Day;

            var localTime = new DateTime(year, month, day, hour, minute, second, millisecond);
            if (localTime > nowInLogTz)
                localTime = localTime.AddDays(-1);

            return TimeZoneInfo.ConvertTimeToUtc(localTime, tz);
        }

        var exact = new DateTime(year, month, day, hour, minute, second, millisecond, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(exact, tz);
    }
}
