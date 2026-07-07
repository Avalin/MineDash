namespace MineDash.Services;

public static class ConsoleCommandFormatter
{
    public static string FormatResponse(string response) => response?.Trim() ?? string.Empty;

    public static string InjectUsername(string command, string username)
    {
        if (string.IsNullOrWhiteSpace(command))
            return command;

        var trimmed = command.Trim();
        if (trimmed.StartsWith('/'))
            trimmed = trimmed[1..];

        if (trimmed.StartsWith("say ", StringComparison.OrdinalIgnoreCase))
        {
            var message = trimmed[4..].TrimStart();
            return $"say [{username}] {message}";
        }

        if (trimmed.Equals("say", StringComparison.OrdinalIgnoreCase))
            return $"say [{username}]";

        return trimmed;
    }
}
