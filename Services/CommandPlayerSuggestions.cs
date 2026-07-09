namespace MineDash.Services;

public enum PlayerSuggestionScope
{
    None,
    OnlinePlayers,
    AllPlayers
}

public readonly record struct PlayerPlaceholderInfo(
    string BaseName,
    PlayerSuggestionScope? ExplicitScope);

public static class CommandPlayerSuggestions
{
    private static readonly HashSet<string> PlayerPlaceholderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "player",
        "target",
        "destination",
        "username",
        "name"
    };

    public static bool IsPlayerPlaceholderName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && PlayerPlaceholderNames.Contains(name);

    public static bool TryParsePlaceholder(string? placeholder, out PlayerPlaceholderInfo info)
    {
        info = default;
        if (string.IsNullOrWhiteSpace(placeholder))
            return false;

        placeholder = placeholder.Trim();
        var colon = placeholder.IndexOf(':');
        if (colon > 0)
        {
            var baseName = placeholder[..colon].Trim();
            var scopeToken = placeholder[(colon + 1)..].Trim();
            if (!IsPlayerPlaceholderName(baseName))
                return false;

            var explicitScope = scopeToken.ToLowerInvariant() switch
            {
                "online" => PlayerSuggestionScope.OnlinePlayers,
                "all" => PlayerSuggestionScope.AllPlayers,
                _ => (PlayerSuggestionScope?)null
            };

            if (explicitScope is null)
                return false;

            info = new PlayerPlaceholderInfo(baseName, explicitScope);
            return true;
        }

        if (!IsPlayerPlaceholderName(placeholder))
            return false;

        info = new PlayerPlaceholderInfo(placeholder, null);
        return true;
    }

    public static string GetDisplayName(string? placeholder)
    {
        if (TryParsePlaceholder(placeholder, out var info))
            return info.BaseName;

        return placeholder?.Trim() ?? string.Empty;
    }

    public static PlayerSuggestionScope Resolve(string commandName, string? placeholder)
    {
        if (!TryParsePlaceholder(placeholder, out var info))
            return PlayerSuggestionScope.None;

        if (info.ExplicitScope is not null)
            return info.ExplicitScope.Value;

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
