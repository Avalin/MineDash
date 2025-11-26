using MineDash.Models;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace MineDash.Services;

public class LogService : ILogService
{
    private readonly ConcurrentDictionary<string, long> _readPositions = new();
    private readonly ConcurrentDictionary<string, DateTime> _lastReadTimes = new();
    private readonly ILogger<LogService> _logger;

    public LogService(ILogger<LogService> logger)
    {
        _logger = logger;
    }

    public async Task<List<LogEntry>> GetRecentLogsAsync(ServerConfig server, int minutes = 30, CancellationToken ct = default)
    {
        var cutoffTime = DateTime.Now.AddMinutes(-minutes);
        var entries = new List<LogEntry>();

        try
        {
            if (string.IsNullOrWhiteSpace(server.LogPath))
            {
                // No log path configured
                _logger.LogDebug("No log path configured for server {ServerName}", server.Name);
                return entries;
            }

            // Trim path to remove any leading/trailing whitespace
            var logPath = server.LogPath?.Trim() ?? string.Empty;
            
            _logger.LogDebug("Loading logs for {ServerName} from {LogPath} (length: {Length})", server.Name, logPath, logPath.Length);

            // Try to access the file directly - File.Exists can sometimes fail even if file is readable
            FileInfo? fileInfo = null;
            try
            {
                // Try File.Exists first
                bool fileExists = File.Exists(logPath);
                _logger.LogDebug("File.Exists({LogPath}) = {Exists}", logPath, fileExists);
                
                // Also try FileInfo
                fileInfo = new FileInfo(logPath);
                bool fileInfoExists = fileInfo.Exists;
                _logger.LogDebug("FileInfo.Exists({LogPath}) = {Exists}, Length: {Length}", logPath, fileInfoExists, fileInfo.Length);
                
                // If both say false, try to open the file anyway (sometimes File.Exists lies)
                if (!fileExists && !fileInfoExists)
                {
                    _logger.LogWarning("File.Exists and FileInfo.Exists both return false for {LogPath}, but will try to open anyway", logPath);
                    // Try to open the file to see if it actually exists
                    try
                    {
                        using var testStream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        _logger.LogInformation("File opened successfully despite File.Exists returning false! File length: {Length}", testStream.Length);
                        fileExists = true;
                        fileInfo = new FileInfo(logPath);
                    }
                    catch (FileNotFoundException)
                    {
                        _logger.LogWarning("File truly does not exist: {LogPath} for server {ServerName}", logPath, server.Name);
                        // Try to list files in directory to help troubleshoot
                        try
                        {
                            var directory = Path.GetDirectoryName(logPath);
                            _logger.LogInformation("Checking directory: {Directory}", directory);
                            if (!string.IsNullOrEmpty(directory))
                            {
                                var dirExists = Directory.Exists(directory);
                                _logger.LogInformation("Directory exists: {DirExists}", dirExists);
                                if (dirExists)
                                {
                                    var files = Directory.GetFiles(directory, "*.log");
                                    _logger.LogInformation("Found {FileCount} .log files. Looking for: {FileName}", files.Length, Path.GetFileName(logPath));
                                    foreach (var file in files.Take(5))
                                    {
                                        _logger.LogInformation("  - {File}", file);
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Error listing files in directory: {Message}", ex.Message);
                        }
                        return entries;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error trying to open file {LogPath}: {Message}", logPath, ex.Message);
                        return entries;
                    }
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogError(ex, "Permission denied accessing log file {LogPath} for server {ServerName}", server.LogPath, server.Name);
                return entries;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Cannot access log file {LogPath} for server {ServerName}", server.LogPath, server.Name);
                return entries;
            }

            // Read from log file (use trimmed path)
            entries = await ReadLogFileAsync(logPath, cutoffTime, ct);
            
            _logger.LogInformation("Loaded {Count} log entries for {ServerName} from {LogPath}", entries.Count, server.Name, logPath);
            
            // Set read position to end of file
            if (fileInfo != null)
            {
                _readPositions[server.Id] = fileInfo.Length;
            }

            _lastReadTimes[server.Id] = DateTime.Now;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading logs for {ServerName} from {LogPath}", server.Name, server.LogPath?.Trim() ?? "");
        }

        return entries;
    }

    public async Task<List<LogEntry>> GetNewLogsAsync(ServerConfig server, CancellationToken ct = default)
    {
        var entries = new List<LogEntry>();
        var lastReadTime = _lastReadTimes.GetValueOrDefault(server.Id, DateTime.MinValue);

        try
        {
            var logPath = server.LogPath?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(logPath))
            {
                return entries;
            }

            // Try to read even if File.Exists says false (sometimes it's wrong)
            bool canRead = false;
            try
            {
                using var testStream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                canRead = true;
            }
            catch (FileNotFoundException)
            {
                return entries;
            }
            catch (Exception)
            {
                // Other errors, try File.Exists as fallback
                canRead = File.Exists(logPath);
            }

            if (!canRead)
            {
                return entries;
            }

            // Read new lines from file
            var fileInfo = new FileInfo(logPath);
            var lastPosition = _readPositions.GetValueOrDefault(server.Id, 0);

            if (fileInfo.Length > lastPosition)
            {
                await using var fileStream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                fileStream.Position = lastPosition;

                using var reader = new StreamReader(fileStream);
                string? line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    var entry = ParseLogLine(line);
                    if (entry != null && entry.Timestamp > lastReadTime)
                    {
                        entries.Add(entry);
                    }
                }

                _readPositions[server.Id] = fileStream.Position;
            }

            if (entries.Any())
            {
                _lastReadTimes[server.Id] = entries.Last().Timestamp;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading new logs for {ServerName} from {LogPath}", server.Name, server.LogPath?.Trim() ?? "");
        }

        return entries;
    }

    public void ResetReadPosition(ServerConfig server)
    {
        _readPositions.TryRemove(server.Id, out _);
        _lastReadTimes.TryRemove(server.Id, out _);
    }

    private async Task<List<LogEntry>> ReadLogFileAsync(string logPath, DateTime since, CancellationToken ct)
    {
        var entries = new List<LogEntry>();

        // Double-check file exists with exception handling
        bool fileExists = false;
        try
        {
            fileExists = File.Exists(logPath);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error checking if file exists {logPath}: {ex.Message}");
            return entries;
        }

        if (!fileExists)
        {
            System.Diagnostics.Debug.WriteLine($"File does not exist (checked): {logPath}");
            return entries;
        }

        try
        {
            await using var fileStream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fileStream);

            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                ct.ThrowIfCancellationRequested();

                var entry = ParseLogLine(line);
                if (entry != null && entry.Timestamp >= since)
                {
                    entries.Add(entry);
                }
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

        // Minecraft log format: [HH:mm:ss] [Thread/Level]: Message
        // Example: [20:24:58] [Server thread/INFO]: There are 0 of a max of 42 players online:
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
            
            // If timestamp is in the future (e.g., it's past midnight), subtract a day
            if (timestamp > DateTime.Now)
            {
                timestamp = timestamp.AddDays(-1);
            }

            return new LogEntry
            {
                Timestamp = timestamp,
                RawLine = line,
                Thread = thread,
                Level = level
            };
        }

        // Fallback: if no timestamp found, use current time
        return new LogEntry
        {
            Timestamp = DateTime.Now,
            RawLine = line,
            Thread = string.Empty,
            Level = string.Empty
        };
    }

}

