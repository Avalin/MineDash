using System.Text.RegularExpressions;
using MineDash.Models;

namespace MineDash.Services;

public interface IServerPlayerService
{
    Task<ServerPlayersResult> GetPlayersAsync(
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

public sealed class ServerPlayersResult
{
    public IReadOnlyList<ServerPlayerInfo> Players { get; init; } = [];
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

    public ServerPlayerService(
        ILogService logService,
        IRconService rconService,
        ILogPlayerHighlighter playerHighlighter,
        IServerOpsService opsService)
    {
        _logService = logService;
        _rconService = rconService;
        _playerHighlighter = playerHighlighter;
        _opsService = opsService;
    }

    public async Task<ServerPlayersResult> GetPlayersAsync(
        ServerConfig server,
        IEnumerable<LogEntry>? liveLogs = null,
        CancellationToken ct = default)
    {
        var connectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (liveLogs is not null)
        {
            foreach (var name in _playerHighlighter.CollectConnectedPlayerNames(liveLogs))
                connectedNames.Add(name);
        }

        var hasLogData = connectedNames.Count > 0;
        var onlineNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? rconError = null;

        var logTask = CollectFromLogsAsync(server, connectedNames, ct);
        var rconTask = CollectOnlinePlayersAsync(server, onlineNames, connectedNames, ct);
        var opsTask = _opsService.GetOperatorIndexAsync(server, liveLogs, ct);

        ServerOperatorIndex operators = ServerOperatorIndex.Empty;
        try
        {
            await Task.WhenAll(logTask, rconTask, opsTask);
            hasLogData = hasLogData || await logTask;
            rconError = await rconTask;
            operators = await opsTask;
        }
        catch (OperationCanceledException)
        {
            rconError ??= "Timed out while loading player data.";
        }

        var players = connectedNames
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

        return new ServerPlayersResult
        {
            Players = players,
            OnlineCount = onlineNames.Count,
            HasLogData = hasLogData,
            OnlineLookupError = rconError
        };
    }

    private async Task<bool> CollectFromLogsAsync(
        ServerConfig server,
        HashSet<string> connectedNames,
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
                connectedNames.Add(name);

            return true;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return connectedNames.Count > 0;
        }
    }

    private async Task<string?> CollectOnlinePlayersAsync(
        ServerConfig server,
        HashSet<string> onlineNames,
        HashSet<string> connectedNames,
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
                connectedNames.Add(name);
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
