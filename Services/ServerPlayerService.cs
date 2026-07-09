using System.Text.RegularExpressions;
using MineDash.Models;

namespace MineDash.Services;

public interface IServerPlayerService
{
    Task<ServerPlayersOverview> GetPlayersOverviewAsync(
        ServerConfig server,
        IEnumerable<LogEntry>? liveLogs = null,
        CancellationToken ct = default);
}

public sealed class ServerPlayerInfo
{
    public required string Name { get; init; }
    public bool IsOnline { get; init; }
    public bool IsOp { get; init; }
}

public enum WhitelistPlayerStatus
{
    Inactive,
    Offline,
    Online
}

public sealed class ServerWhitelistEntry
{
    public required string Uuid { get; init; }
    public required string DisplayName { get; init; }
    public WhitelistPlayerStatus Status { get; init; }
    public bool IsOp { get; init; }
}

public sealed class ServerBanListEntry
{
    public required string Uuid { get; init; }
    public required string DisplayName { get; init; }
    public string? Reason { get; init; }
}

public sealed class ServerPlayersOverview
{
    public IReadOnlyList<ServerPlayerInfo> Players { get; init; } = [];
    public IReadOnlyList<ServerWhitelistEntry> Whitelist { get; init; } = [];
    public IReadOnlyList<ServerBanListEntry> Bans { get; init; } = [];
    public int OnlineCount { get; init; }
    public bool HasLogData { get; init; }
    public string? OnlineLookupError { get; init; }
}

public sealed class ServerPlayerService : IServerPlayerService
{
    private const int HistoryTailLines = 20_000;
    private static readonly TimeSpan RconTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan LogReadTimeout = TimeSpan.FromSeconds(8);

    private static readonly Regex ListResponseRegex = new(
        @"There are \d+ of a max of \d+ players? online:?\s*(?<names>.*)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly ILogService _logService;
    private readonly IRconService _rconService;
    private readonly ILogPlayerHighlighter _playerHighlighter;
    private readonly IServerOpsService _opsService;
    private readonly IServerUserCacheService _userCacheService;
    private readonly IServerAccessListService _accessListService;

    public ServerPlayerService(
        ILogService logService,
        IRconService rconService,
        ILogPlayerHighlighter playerHighlighter,
        IServerOpsService opsService,
        IServerUserCacheService userCacheService,
        IServerAccessListService accessListService)
    {
        _logService = logService;
        _rconService = rconService;
        _playerHighlighter = playerHighlighter;
        _opsService = opsService;
        _userCacheService = userCacheService;
        _accessListService = accessListService;
    }

    public async Task<ServerPlayersOverview> GetPlayersOverviewAsync(
        ServerConfig server,
        IEnumerable<LogEntry>? liveLogs = null,
        CancellationToken ct = default)
    {
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (liveLogs is not null)
        {
            foreach (var name in _playerHighlighter.CollectConnectedPlayerNames(liveLogs))
                seenNames.Add(name);
        }

        var hasLogData = seenNames.Count > 0;
        var onlineNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? rconError = null;

        var userCacheTask = _userCacheService.LoadAsync(server, ct);
        var logTask = CollectFromLogsAsync(server, seenNames, ct);
        var rconTask = CollectOnlinePlayersAsync(server, onlineNames, seenNames, ct);
        var whitelistTask = _accessListService.LoadWhitelistAsync(server, ct);
        var bansTask = _accessListService.LoadBansAsync(server, ct);

        ServerUserCacheIndex userCache = ServerUserCacheIndex.Empty;
        IReadOnlyList<ServerProfileEntry> whitelistProfiles = [];
        IReadOnlyList<ServerBanEntry> banProfiles = [];

        try
        {
            await Task.WhenAll(userCacheTask, logTask, rconTask, whitelistTask, bansTask);
            userCache = await userCacheTask;
            hasLogData = hasLogData || await logTask;
            rconError = await rconTask;
            whitelistProfiles = await whitelistTask;
            banProfiles = await bansTask;
        }
        catch (OperationCanceledException)
        {
            rconError ??= "Timed out while loading player data.";
        }

        var operators = await _opsService.GetOperatorIndexAsync(server, userCache, liveLogs, ct);

        var players = seenNames
            .Select(n => new ServerPlayerInfo
            {
                Name = n,
                IsOnline = onlineNames.Contains(n),
                IsOp = operators.IsOperator(n)
            })
            .OrderByDescending(p => p.IsOp)
            .ThenByDescending(p => p.IsOnline)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var whitelist = whitelistProfiles
            .Select(entry => BuildWhitelistEntry(entry, userCache, seenNames, onlineNames, operators))
            .OrderByDescending(e => e.Status == WhitelistPlayerStatus.Online)
            .ThenByDescending(e => e.Status == WhitelistPlayerStatus.Offline)
            .ThenBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var bans = banProfiles
            .Select(entry => new ServerBanListEntry
            {
                Uuid = entry.Uuid,
                DisplayName = ResolveDisplayName(entry.Name, entry.Uuid, userCache),
                Reason = entry.Reason
            })
            .OrderBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ServerPlayersOverview
        {
            Players = players,
            Whitelist = whitelist,
            Bans = bans,
            OnlineCount = onlineNames.Count,
            HasLogData = hasLogData,
            OnlineLookupError = rconError
        };
    }

    private static ServerWhitelistEntry BuildWhitelistEntry(
        ServerProfileEntry entry,
        ServerUserCacheIndex userCache,
        HashSet<string> seenNames,
        HashSet<string> onlineNames,
        ServerOperatorIndex operators)
    {
        var displayName = ResolveDisplayName(entry.Name, entry.Uuid, userCache);
        var hasJoined = HasEverJoined(entry.Uuid, displayName, userCache, seenNames);
        var isOnline = hasJoined && IsOnline(entry.Uuid, displayName, userCache, onlineNames);

        return new ServerWhitelistEntry
        {
            Uuid = entry.Uuid,
            DisplayName = displayName,
            Status = !hasJoined
                ? WhitelistPlayerStatus.Inactive
                : isOnline
                    ? WhitelistPlayerStatus.Online
                    : WhitelistPlayerStatus.Offline,
            IsOp = operators.IsOperatorUuid(entry.Uuid) || operators.IsOperator(displayName)
        };
    }

    private static string ResolveDisplayName(string name, string uuid, ServerUserCacheIndex userCache)
    {
        if (!string.IsNullOrWhiteSpace(name))
            return name.Trim();

        return userCache.ResolveName(uuid) ?? uuid;
    }

    private static bool HasEverJoined(
        string uuid,
        string displayName,
        ServerUserCacheIndex userCache,
        HashSet<string> seenNames)
    {
        if (userCache.HasJoined(uuid))
            return true;

        if (seenNames.Contains(displayName))
            return true;

        var cachedName = userCache.ResolveName(uuid);
        return cachedName is not null && seenNames.Contains(cachedName);
    }

    private static bool IsOnline(
        string uuid,
        string displayName,
        ServerUserCacheIndex userCache,
        HashSet<string> onlineNames)
    {
        if (onlineNames.Contains(displayName))
            return true;

        var cachedName = userCache.ResolveName(uuid);
        if (cachedName is not null && onlineNames.Contains(cachedName))
            return true;

        foreach (var onlineName in onlineNames)
        {
            var onlineUuid = userCache.ResolveUuid(onlineName);
            if (onlineUuid is not null && onlineUuid.Equals(uuid, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private async Task<bool> CollectFromLogsAsync(
        ServerConfig server,
        HashSet<string> seenNames,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(server.LogPath))
            return false;

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(LogReadTimeout);

        try
        {
            var (entries, _) = await _logService.GetRecentLogsAsync(server, HistoryTailLines, timeoutCts.Token);
            if (entries.Count == 0)
                return false;

            foreach (var name in _playerHighlighter.CollectConnectedPlayerNames(entries))
                seenNames.Add(name);

            return true;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return seenNames.Count > 0;
        }
    }

    private async Task<string?> CollectOnlinePlayersAsync(
        ServerConfig server,
        HashSet<string> onlineNames,
        HashSet<string> seenNames,
        CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(RconTimeout);

        try
        {
            var response = await _rconService.SendCommandAsync(server, "list", timeoutCts.Token);
            foreach (var name in ParseOnlinePlayers(response))
            {
                onlineNames.Add(name);
                seenNames.Add(name);
            }

            return null;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return "Server did not respond in time.";
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    internal static IReadOnlyList<string> ParseOnlinePlayers(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return [];

        var match = ListResponseRegex.Match(response.Trim());
        if (!match.Success)
            return [];

        var namesPart = match.Groups["names"].Value.Trim();
        if (string.IsNullOrEmpty(namesPart))
            return [];

        return namesPart.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }
}
