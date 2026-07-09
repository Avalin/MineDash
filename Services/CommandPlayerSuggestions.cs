namespace MineDash.Services;

public enum PlayerSuggestionScope
{
    None,
    OnlinePlayers,
    AllPlayers
}

public static class CommandPlayerSuggestions
{
    private static readonly HashSet<string> PlayerPlaceholders = new(StringComparer.OrdinalIgnoreCase)
    {
        "player",
        "target",
        "destination",
        "username",
        "name"
    };

    public static bool IsPlayerPlaceholder(string? placeholder) =>
        !string.IsNullOrWhiteSpace(placeholder) && PlayerPlaceholders.Contains(placeholder);

    public static PlayerSuggestionScope Resolve(string commandName, string? placeholder)
    {
        if (!IsPlayerPlaceholder(placeholder))
            return PlayerSuggestionScope.None;

        return commandName.Trim().ToLowerInvariant() switch
        {
            "kick" => PlayerSuggestionScope.OnlinePlayers,
            "tp" or "teleport" => PlayerSuggestionScope.OnlinePlayers,
            _ => PlayerSuggestionScope.AllPlayers
        };
    }

    public static IReadOnlyList<string> GetNames(
        ServerPlayersOverview overview,
        PlayerSuggestionScope scope)
    {
        return scope switch
        {
            PlayerSuggestionScope.OnlinePlayers => overview.Players
                .Where(p => p.IsOnline)
                .Select(p => p.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList(),

            PlayerSuggestionScope.AllPlayers => CollectAllKnownNames(overview),

            _ => []
        };
    }

    private static List<string> CollectAllKnownNames(ServerPlayersOverview overview)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var player in overview.Players)
            names.Add(player.Name);

        foreach (var entry in overview.Whitelist)
            names.Add(entry.DisplayName);

        return names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
