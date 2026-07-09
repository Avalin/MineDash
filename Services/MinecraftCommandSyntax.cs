using MineDash.Models;

namespace MineDash.Services;

public static class MinecraftCommandSyntax
{
    public static CommandSyntaxSchema GetSchema(MinecraftCommand cmd) =>
        CommandSyntaxParser.Parse(cmd.Syntax);

    public static string GetCommandName(MinecraftCommand cmd) =>
        GetSchema(cmd).CommandName;

    public static string GetDisplayName(MinecraftCommand cmd) => GetCommandName(cmd);

    public static bool AcceptsArguments(MinecraftCommand cmd) =>
        GetSchema(cmd).HasUserInput;

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

        return CommandSyntaxParser.BuildCommand(GetSchema(cmd), state.CommandArgValues);
    }

    public static bool CanSend(ConsoleState state, MinecraftCommand? cmd)
    {
        if (cmd is null)
            return false;

        return CommandSyntaxParser.CanSend(GetSchema(cmd), state.CommandArgValues);
    }

    public static void ClearArguments(ConsoleState state)
    {
        state.CommandArguments = string.Empty;
        state.CommandArgValues.Clear();
    }

    public static void ResetArgumentsForCommand(ConsoleState state, MinecraftCommand cmd)
    {
        ClearArguments(state);
        EnsureDefaultArgValues(state, cmd);
    }

    public static void EnsureDefaultArgValues(ConsoleState state, MinecraftCommand cmd)
    {
        foreach (var arg in GetSchema(cmd).Args)
        {
            if (arg.Kind != CommandArgKind.Choice || !arg.RequiresUserInput || arg.Choices is not { Length: > 0 })
                continue;

            if (!state.CommandArgValues.ContainsKey(arg.Index))
                SetArgValue(state, arg.Index, arg.Choices[0]);
        }
    }

    public static string GetArgValue(ConsoleState state, int index) =>
        state.CommandArgValues.TryGetValue(index, out var value) ? value : string.Empty;

    public static void SetArgValue(ConsoleState state, int index, string? value)
    {
        if (string.IsNullOrEmpty(value))
            state.CommandArgValues.Remove(index);
        else
            state.CommandArgValues[index] = value;
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
            var schema = GetSchema(best);
            if (!CommandSyntaxParser.TryApplyHistory(historyCmd, schema, state.CommandArgValues))
            {
                ClearArguments(state);
                var name = schema.CommandName;
                state.CommandArguments = historyCmd.Equals(name, StringComparison.OrdinalIgnoreCase)
                    ? string.Empty
                    : historyCmd[(name.Length + 1)..];
            }
            else
            {
                state.CommandArguments = string.Empty;
            }

            return;
        }

        if (defaultSayCommandId is not null)
        {
            state.SelectedCommandId = defaultSayCommandId;
            ClearArguments(state);
            state.CommandArguments = historyCmd;
        }
    }
}
