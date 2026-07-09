using System.Text.Json;
using System.Text.RegularExpressions;
using MineDash.Models;

namespace MineDash.Services;

public interface IServerOpsService
{
    Task<IReadOnlySet<string>> GetOperatorNamesAsync(
        ServerConfig server,
        IEnumerable<LogEntry>? liveLogs = null,
        CancellationToken ct = default);
}

public sealed class ServerOpsService : IServerOpsService
{
    private const int HistoryTailLines = 20_000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly Regex MadeOpRegex = new(
        @"Made (?<name>.+?) a server operator",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex RemovedOpRegex = new(
        @"Made (?<name>.+?) no longer a server operator",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex IssuedOpCommandRegex = new(
        @"issued server command:\s*/(?:minecraft:)?op\s+(?<name>\S+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly ILogService _logService;

    public ServerOpsService(ILogService logService)
    {
        _logService = logService;
    }

    public async Task<IReadOnlySet<string>> GetOperatorNamesAsync(
        ServerConfig server,
        IEnumerable<LogEntry>? liveLogs = null,
        CancellationToken ct = default)
    {
        var opNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var userCache = await LoadUserCacheAsync(server, ct);

        await LoadFromOpsFileAsync(server, opNames, userCache, ct);
        await LoadFromLogsAsync(server, liveLogs, opNames, ct);

        return opNames;
    }

    private static async Task LoadFromOpsFileAsync(
        ServerConfig server,
        HashSet<string> opNames,
        UserCacheIndex userCache,
        CancellationToken ct)
    {
        foreach (var path in ServerPathResolver.GetOpsJsonCandidates(server))
        {
            ct.ThrowIfCancellationRequested();
            if (!File.Exists(path))
                continue;

            try
            {
                await using var stream = File.OpenRead(path);
                var entries = await JsonSerializer.DeserializeAsync<List<OpsEntry>>(stream, JsonOptions, ct);
                if (entries is null)
                    continue;

                foreach (var entry in entries)
                {
                    if (!string.IsNullOrWhiteSpace(entry.Name))
                    {
                        opNames.Add(entry.Name.Trim());
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(entry.Uuid))
                        continue;

                    var resolved = userCache.ResolveName(entry.Uuid);
                    if (resolved is not null)
                        opNames.Add(resolved);
                }

                return;
            }
            catch
            {
                // Try the next candidate path.
            }
        }
    }

    private async Task LoadFromLogsAsync(
        ServerConfig server,
        IEnumerable<LogEntry>? liveLogs,
        HashSet<string> opNames,
        CancellationToken ct)
    {
        if (liveLogs is not null)
            ApplyLogOperatorChanges(liveLogs, opNames);

        if (string.IsNullOrWhiteSpace(server.LogPath))
            return;

        try
        {
            var (entries, _) = await _logService.GetRecentLogsAsync(server, HistoryTailLines, ct);
            ApplyLogOperatorChanges(entries, opNames);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
        }
    }

    private static void ApplyLogOperatorChanges(IEnumerable<LogEntry> logs, HashSet<string> opNames)
    {
        string? pendingOpTarget = null;

        foreach (var log in logs)
        {
            var message = string.IsNullOrWhiteSpace(log.Message) ? log.RawLine : log.Message;
            if (string.IsNullOrWhiteSpace(message))
                continue;

            var issuedOp = IssuedOpCommandRegex.Match(message);
            if (issuedOp.Success)
            {
                var target = issuedOp.Groups["name"].Value.Trim();
                pendingOpTarget = IsValidPlayerName(target) ? target : null;
                continue;
            }

            if (pendingOpTarget is not null
                && message.Contains("already is an operator", StringComparison.OrdinalIgnoreCase))
            {
                opNames.Add(pendingOpTarget);
                pendingOpTarget = null;
                continue;
            }

            var removed = RemovedOpRegex.Match(message);
            if (removed.Success)
            {
                var name = removed.Groups["name"].Value.Trim();
                if (IsValidPlayerName(name))
                    opNames.Remove(name);

                pendingOpTarget = null;
                continue;
            }

            var made = MadeOpRegex.Match(message);
            if (!made.Success)
                continue;

            var opName = made.Groups["name"].Value.Trim();
            if (IsValidPlayerName(opName))
                opNames.Add(opName);

            pendingOpTarget = null;
        }
    }

    private static async Task<UserCacheIndex> LoadUserCacheAsync(ServerConfig server, CancellationToken ct)
    {
        foreach (var path in ServerPathResolver.GetUserCacheCandidates(server))
        {
            ct.ThrowIfCancellationRequested();
            if (!File.Exists(path))
                continue;

            try
            {
                await using var stream = File.OpenRead(path);
                var entries = await JsonSerializer.DeserializeAsync<List<UserCacheEntry>>(stream, JsonOptions, ct);
                if (entries is null || entries.Count == 0)
                    continue;

                return UserCacheIndex.From(entries);
            }
            catch
            {
                // Try the next candidate path.
            }
        }

        return UserCacheIndex.Empty;
    }

    private static bool IsValidPlayerName(string name) =>
        name.Length is >= 3 and <= 16
        && name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');

    private sealed class OpsEntry
    {
        public string Name { get; set; } = string.Empty;
        public string Uuid { get; set; } = string.Empty;
    }

    private sealed class UserCacheEntry
    {
        public string Name { get; set; } = string.Empty;
        public string Uuid { get; set; } = string.Empty;
    }

    private sealed class UserCacheIndex
    {
        public static UserCacheIndex Empty { get; } = new([]);

        private readonly Dictionary<string, string> _uuidToName;

        private UserCacheIndex(Dictionary<string, string> uuidToName) =>
            _uuidToName = uuidToName;

        public static UserCacheIndex From(IEnumerable<UserCacheEntry> entries)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries)
            {
                if (string.IsNullOrWhiteSpace(entry.Uuid) || string.IsNullOrWhiteSpace(entry.Name))
                    continue;

                map[NormalizeUuid(entry.Uuid)] = entry.Name.Trim();
            }

            return new UserCacheIndex(map);
        }

        public string? ResolveName(string uuid) =>
            _uuidToName.TryGetValue(NormalizeUuid(uuid), out var name) ? name : null;

        private static string NormalizeUuid(string uuid) =>
            uuid.Replace("-", string.Empty, StringComparison.Ordinal);
    }
}
