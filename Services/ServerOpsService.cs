using System.Text.Json;
using System.Text.RegularExpressions;
using MineDash.Models;

namespace MineDash.Services;

public interface IServerOpsService
{
    Task<ServerOperatorIndex> GetOperatorIndexAsync(
        ServerConfig server,
        IEnumerable<LogEntry>? liveLogs = null,
        CancellationToken ct = default);
}

internal sealed class UserCacheIndex
{
    public static UserCacheIndex Empty { get; } = new([], []);

    private readonly Dictionary<string, string> _nameToUuid;

    private UserCacheIndex(
        Dictionary<string, string> uuidToName,
        Dictionary<string, string> nameToUuid)
    {
        _ = uuidToName;
        _nameToUuid = nameToUuid;
    }

    public static UserCacheIndex From(IEnumerable<UserCacheEntry> entries)
    {
        var uuidToName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var nameToUuid = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Uuid) || string.IsNullOrWhiteSpace(entry.Name))
                continue;

            var uuid = entry.Uuid.Replace("-", string.Empty, StringComparison.Ordinal);
            var name = entry.Name.Trim();

            uuidToName[uuid] = name;
            nameToUuid[name] = uuid;
        }

        return new UserCacheIndex(uuidToName, nameToUuid);
    }

    public string? ResolveUuid(string name) =>
        _nameToUuid.TryGetValue(name.Trim(), out var uuid) ? uuid : null;

    internal sealed class UserCacheEntry
    {
        public string Name { get; set; } = string.Empty;
        public string Uuid { get; set; } = string.Empty;
    }
}

public sealed class ServerOperatorIndex
{
    internal static ServerOperatorIndex Empty { get; } = new(
        new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        UserCacheIndex.Empty);

    private readonly HashSet<string> _uuids;
    private readonly HashSet<string> _nameFallbacks;
    private readonly UserCacheIndex _userCache;

    internal ServerOperatorIndex(
        HashSet<string> uuids,
        HashSet<string> nameFallbacks,
        UserCacheIndex userCache)
    {
        _uuids = uuids;
        _nameFallbacks = nameFallbacks;
        _userCache = userCache;
    }

    public bool IsOperator(string playerName)
    {
        if (string.IsNullOrWhiteSpace(playerName))
            return false;

        if (_nameFallbacks.Contains(playerName))
            return true;

        var uuid = _userCache.ResolveUuid(playerName);
        return uuid is not null && _uuids.Contains(uuid);
    }
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

    public async Task<ServerOperatorIndex> GetOperatorIndexAsync(
        ServerConfig server,
        IEnumerable<LogEntry>? liveLogs = null,
        CancellationToken ct = default)
    {
        var opUuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var nameFallbacks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var userCache = await LoadUserCacheAsync(server, ct);

        await LoadFromOpsFileAsync(server, opUuids, nameFallbacks, ct);
        await LoadFromLogsAsync(server, liveLogs, nameFallbacks, ct);

        return new ServerOperatorIndex(opUuids, nameFallbacks, userCache);
    }

    private static async Task LoadFromOpsFileAsync(
        ServerConfig server,
        HashSet<string> opUuids,
        HashSet<string> nameFallbacks,
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
                    if (!string.IsNullOrWhiteSpace(entry.Uuid))
                    {
                        opUuids.Add(NormalizeUuid(entry.Uuid));
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(entry.Name))
                        nameFallbacks.Add(entry.Name.Trim());
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
        HashSet<string> nameFallbacks,
        CancellationToken ct)
    {
        if (liveLogs is not null)
            ApplyLogOperatorChanges(liveLogs, nameFallbacks);

        if (string.IsNullOrWhiteSpace(server.LogPath))
            return;

        try
        {
            var (entries, _) = await _logService.GetRecentLogsAsync(server, HistoryTailLines, ct);
            ApplyLogOperatorChanges(entries, nameFallbacks);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
        }
    }

    private static void ApplyLogOperatorChanges(IEnumerable<LogEntry> logs, HashSet<string> nameFallbacks)
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
                nameFallbacks.Add(pendingOpTarget);
                pendingOpTarget = null;
                continue;
            }

            var removed = RemovedOpRegex.Match(message);
            if (removed.Success)
            {
                var name = removed.Groups["name"].Value.Trim();
                if (IsValidPlayerName(name))
                    nameFallbacks.Remove(name);

                pendingOpTarget = null;
                continue;
            }

            var made = MadeOpRegex.Match(message);
            if (!made.Success)
                continue;

            var opName = made.Groups["name"].Value.Trim();
            if (IsValidPlayerName(opName))
                nameFallbacks.Add(opName);

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
                var entries = await JsonSerializer.DeserializeAsync<List<UserCacheIndex.UserCacheEntry>>(stream, JsonOptions, ct);
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

    private static string NormalizeUuid(string uuid) =>
        uuid.Replace("-", string.Empty, StringComparison.Ordinal);

    private sealed class OpsEntry
    {
        public string Name { get; set; } = string.Empty;
        public string Uuid { get; set; } = string.Empty;
    }
}
