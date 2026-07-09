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

    private static readonly Regex ListResponseRegex = new(
        @"There are \d+ of a max of \d+ players? online:?\s*(?<names>.*)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly ILogService _logService;
    private readonly IRconService _rconService;
    private readonly ILogPlayerHighlighter _playerHighlighter;

    public ServerPlayerService(
        ILogService logService,
        IRconService rconService,
        ILogPlayerHighlighter playerHighlighter)
    {
        _logService = logService;
        _rconService = rconService;
        _playerHighlighter = playerHighlighter;
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

        var hasLogData = false;
        if (!string.IsNullOrWhiteSpace(server.LogPath))
        {
            var (entries, _) = await _logService.GetRecentLogsAsync(server, HistoryTailLines, ct);
            if (entries.Count > 0)
            {
                hasLogData = true;
                foreach (var name in _playerHighlighter.CollectConnectedPlayerNames(entries))
                    connectedNames.Add(name);
            }
        }

        var onlineNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? rconError = null;

        try
        {
            var response = await _rconService.SendCommandAsync(server, "list", ct);
            foreach (var name in ParseOnlinePlayers(response))
            {
                onlineNames.Add(name);
                connectedNames.Add(name);
            }
        }
        catch (Exception ex)
        {
            rconError = ex.Message;
        }

        var players = connectedNames
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .Select(n => new ServerPlayerInfo
            {
                Name = n,
                IsOnline = onlineNames.Contains(n)
            })
            .ToList();

        return new ServerPlayersResult
        {
            Players = players,
            OnlineCount = onlineNames.Count,
            HasLogData = hasLogData,
            OnlineLookupError = rconError
        };
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
