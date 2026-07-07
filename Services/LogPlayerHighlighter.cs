using System.Net;
using System.Text.RegularExpressions;

namespace MineDash.Services;

public interface ILogPlayerHighlighter
{
    IReadOnlySet<string> CollectPlayerNames(IEnumerable<LogEntry> logs);
    string Highlight(string message, IReadOnlySet<string> playerNames);
}

public sealed class LogPlayerHighlighter : ILogPlayerHighlighter
{
    private static readonly Regex AnsiRegex = new(@"\x1b\[[0-9;]*m", RegexOptions.Compiled);
    private static readonly Regex TrailingResetRegex = new(@"\[0m\]?$", RegexOptions.Compiled);

    private static readonly (Regex Pattern, int Group)[] ExtractionPatterns =
    [
        (new(@"^<(?<name>[^>]+)>", RegexOptions.Compiled), 1),
        (new(@"^(?<name>.+?)\[/[\d.:]+\]", RegexOptions.Compiled), 1),
        (new(@"^(?<name>.+?) joined the game$", RegexOptions.Compiled), 1),
        (new(@"^(?<name>.+?) left the game$", RegexOptions.Compiled), 1),
        (new(@"^(?<name>.+?) lost connection", RegexOptions.Compiled), 1),
        (new(@"Disconnecting client (?<name>\S+)", RegexOptions.Compiled), 1),
        (new(@"(?:with player|player:)\s+(?<name>\S+)", RegexOptions.Compiled | RegexOptions.IgnoreCase), 1),
    ];

    public IReadOnlySet<string> CollectPlayerNames(IEnumerable<LogEntry> logs)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var log in logs)
        {
            var message = GetMessageText(log);
            if (string.IsNullOrWhiteSpace(message))
                continue;

            foreach (var name in ExtractNames(message))
                names.Add(name);
        }

        return names;
    }

    public string Highlight(string message, IReadOnlySet<string> playerNames)
    {
        var cleaned = CleanMessage(message);
        var encoded = WebUtility.HtmlEncode(cleaned);

        if (playerNames.Count == 0)
            return encoded;

        var validNames = playerNames
            .Where(IsValidPlayerName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(n => n.Length)
            .Select(Regex.Escape)
            .ToList();

        if (validNames.Count == 0)
            return encoded;

        var pattern = $@"\b(?:{string.Join("|", validNames)})\b";
        return Regex.Replace(
            encoded,
            pattern,
            m => $"<span class=\"log-player-name\">{m.Value}</span>",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string GetMessageText(LogEntry log) =>
        string.IsNullOrWhiteSpace(log.Message) ? log.RawLine : log.Message;

    private static IEnumerable<string> ExtractNames(string message)
    {
        var cleaned = CleanMessage(message);

        foreach (var (pattern, group) in ExtractionPatterns)
        {
            var match = pattern.Match(cleaned);
            if (!match.Success)
                continue;

            var name = match.Groups[group].Value.Trim();
            if (IsValidPlayerName(name))
                yield return name;
        }
    }

    private static string CleanMessage(string message)
    {
        if (string.IsNullOrEmpty(message))
            return string.Empty;

        var cleaned = AnsiRegex.Replace(message, string.Empty);
        return TrailingResetRegex.Replace(cleaned, string.Empty).TrimEnd();
    }

    private static bool IsValidPlayerName(string name) =>
        name.Length is >= 3 and <= 16
        && name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');
}
