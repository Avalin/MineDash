using MineDash.Models;

namespace MineDash.Services;

public static class MinecraftCommandSyntax
{
    public static string GetCommandName(MinecraftCommand cmd)
    {
        var syntax = NormalizeSyntax(cmd.Syntax);
        var delimiter = syntax.IndexOfAny([' ', '<']);
        return delimiter > 0 ? syntax[..delimiter] : syntax;
    }

    public static string GetArgumentsHint(MinecraftCommand cmd)
    {
        var syntax = NormalizeSyntax(cmd.Syntax);
        var space = syntax.IndexOf(' ');
        return space < 0 ? string.Empty : syntax[(space + 1)..];
    }

    public static string GetDisplayName(MinecraftCommand cmd) => GetCommandName(cmd);

    public static bool AcceptsArguments(MinecraftCommand cmd) =>
        NormalizeSyntax(cmd.Syntax).Contains(' ');

    public static IEnumerable<MinecraftCommand> OrderForDisplay(IEnumerable<MinecraftCommand> commands) =>
        commands
            .OrderBy(c => GetCommandName(c).Equals("say", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(c => GetCommandName(c), StringComparer.OrdinalIgnoreCase);

    public static MinecraftCommand? FindMatchingCommand(
        IReadOnlyList<MinecraftCommand> commands, string historyCmd)
    {
        historyCmd = historyCmd.Trim();
        if (historyCmd.StartsWith('/'))
            historyCmd = historyCmd[1..];

        foreach (var cmd in commands.OrderByDescending(c => GetCommandName(c).Length))
        {
            var name = GetCommandName(cmd);
            if (historyCmd.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                historyCmd.StartsWith(name + " ", StringComparison.OrdinalIgnoreCase))
            {
                return cmd;
            }
        }

        return null;
    }

    public static string ResolveSelectedCommandId(
        ConsoleState state,
        IReadOnlyList<MinecraftCommand> commands,
        string? defaultSayCommandId)
    {
        if (!string.IsNullOrEmpty(state.SelectedCommandId) &&
            commands.Any(c => c.Id == state.SelectedCommandId))
        {
            return state.SelectedCommandId;
        }

        return defaultSayCommandId ?? commands.FirstOrDefault()?.Id ?? string.Empty;
    }

    public static MinecraftCommand? GetSelectedCommand(
        ConsoleState state,
        IReadOnlyList<MinecraftCommand> commands,
        string? defaultSayCommandId)
    {
        if (commands.Count == 0)
            return null;

        var id = ResolveSelectedCommandId(state, commands, defaultSayCommandId);
        return commands.FirstOrDefault(c => c.Id == id) ?? commands.FirstOrDefault();
    }

    public static string BuildCommand(ConsoleState state, MinecraftCommand? cmd)
    {
        if (cmd is null)
            return string.Empty;

        var name = GetCommandName(cmd);
        var args = state.CommandArguments.Trim();
        return string.IsNullOrEmpty(args) ? name : $"{name} {args}";
    }

    public static bool CanSend(ConsoleState state, MinecraftCommand? cmd)
    {
        if (cmd is null)
            return false;

        return !AcceptsArguments(cmd) || !string.IsNullOrWhiteSpace(state.CommandArguments);
    }

    public static void ApplyHistoryCommand(
        ConsoleState state,
        IReadOnlyList<MinecraftCommand> commands,
        string? defaultSayCommandId,
        string historyCmd)
    {
        var best = FindMatchingCommand(commands, historyCmd);
        historyCmd = historyCmd.Trim();
        if (historyCmd.StartsWith('/'))
            historyCmd = historyCmd[1..];

        if (best is not null)
        {
            state.SelectedCommandId = best.Id;
            var name = GetCommandName(best);
            state.CommandArguments = historyCmd.Equals(name, StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : historyCmd[(name.Length + 1)..];
            return;
        }

        if (defaultSayCommandId is not null)
        {
            state.SelectedCommandId = defaultSayCommandId;
            state.CommandArguments = historyCmd;
        }
    }

    private static string NormalizeSyntax(string syntax)
    {
        syntax = syntax.Trim();
        return syntax.StartsWith('/') ? syntax[1..] : syntax;
    }
}
