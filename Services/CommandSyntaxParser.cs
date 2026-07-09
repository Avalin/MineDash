namespace MineDash.Services;

public enum CommandArgKind
{
    Literal,
    Choice,
    Text,
    Message
}

public sealed class CommandArgPart
{
    public required CommandArgKind Kind { get; init; }
    public bool IsOptional { get; init; }
    public string? Literal { get; init; }
    public string[]? Choices { get; init; }
    public string? Placeholder { get; init; }
    public int Index { get; init; }

    public bool RequiresUserInput =>
        Kind is not CommandArgKind.Literal && !IsOptional;

    public string InputPlaceholder => IsOptional
        ? string.IsNullOrEmpty(Placeholder) ? "(optional)" : $"{Placeholder} (optional)"
        : Placeholder ?? string.Empty;

    public string ChoiceSizerText
    {
        get
        {
            var widest = Choices?.OrderByDescending(c => c.Length).FirstOrDefault() ?? string.Empty;
            if (IsOptional && InputPlaceholder.Length > widest.Length)
                return InputPlaceholder;

            return widest;
        }
    }
}

public sealed class CommandSyntaxSchema
{
    public required string CommandName { get; init; }
    public IReadOnlyList<CommandArgPart> Args { get; init; } = [];

    public bool HasUserInput => Args.Any(a => a.RequiresUserInput);

    public IEnumerable<CommandArgPart> UserArgs => Args.Where(a => a.Kind != CommandArgKind.Literal);
}

public static class CommandSyntaxParser
{
    private static readonly HashSet<string> MessagePlaceholderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "message",
        "reason"
    };

    public static CommandSyntaxSchema Parse(string syntax)
    {
        syntax = NormalizeSyntax(syntax);
        var tokens = Tokenize(syntax);
        if (tokens.Count == 0)
            return new CommandSyntaxSchema { CommandName = string.Empty };

        var commandName = tokens[0];
        var args = new List<CommandArgPart>();
        var userArgIndex = 0;

        for (var i = 1; i < tokens.Count; i++)
        {
            var part = ParseToken(tokens[i], userArgIndex);
            if (part.Kind != CommandArgKind.Literal)
                userArgIndex++;

            args.Add(part);
        }

        return new CommandSyntaxSchema
        {
            CommandName = commandName,
            Args = args
        };
    }

    public static bool TryApplyHistory(string historyCmd, CommandSyntaxSchema schema, IDictionary<int, string> values)
    {
        values.Clear();
        var remaining = historyCmd.Trim();
        if (remaining.StartsWith('/'))
            remaining = remaining[1..].TrimStart();

        if (!remaining.StartsWith(schema.CommandName, StringComparison.OrdinalIgnoreCase))
            return false;

        remaining = remaining[schema.CommandName.Length..].TrimStart();

        if (string.IsNullOrEmpty(remaining))
            return schema.Args.All(a => a.IsOptional || a.Kind == CommandArgKind.Literal);

        foreach (var arg in schema.Args)
        {
            if (string.IsNullOrWhiteSpace(remaining))
            {
                if (arg.RequiresUserInput)
                    return false;

                continue;
            }

            switch (arg.Kind)
            {
                case CommandArgKind.Literal:
                    if (!TryConsumeLiteral(ref remaining, arg.Literal!))
                        return false;
                    break;

                case CommandArgKind.Choice:
                {
                    var choice = TryConsumeChoice(ref remaining, arg.Choices!);
                    if (choice is null)
                        return arg.IsOptional;

                    values[arg.Index] = choice;
                    break;
                }

                case CommandArgKind.Text:
                {
                    var word = TryConsumeWord(ref remaining);
                    if (word is null)
                        return arg.IsOptional;

                    values[arg.Index] = word;
                    break;
                }

                case CommandArgKind.Message:
                    values[arg.Index] = remaining;
                    remaining = string.Empty;
                    break;
            }
        }

        return string.IsNullOrWhiteSpace(remaining);
    }

    public static string BuildCommand(CommandSyntaxSchema schema, IReadOnlyDictionary<int, string> values)
    {
        var parts = new List<string> { schema.CommandName };

        foreach (var arg in schema.Args)
        {
            switch (arg.Kind)
            {
                case CommandArgKind.Literal:
                    parts.Add(arg.Literal!);
                    break;

                case CommandArgKind.Choice:
                case CommandArgKind.Text:
                case CommandArgKind.Message:
                    if (!values.TryGetValue(arg.Index, out var value))
                        break;

                    value = value.Trim();
                    if (!string.IsNullOrEmpty(value) || arg.RequiresUserInput)
                        parts.Add(value);
                    break;
            }
        }

        return string.Join(' ', parts.Where(p => !string.IsNullOrWhiteSpace(p)));
    }

    public static bool CanSend(CommandSyntaxSchema schema, IReadOnlyDictionary<int, string> values)
    {
        if (!schema.HasUserInput)
            return true;

        foreach (var arg in schema.Args)
        {
            if (!arg.RequiresUserInput)
                continue;

            if (!values.TryGetValue(arg.Index, out var value) || string.IsNullOrWhiteSpace(value))
                return false;
        }

        return true;
    }

    private static CommandArgPart ParseToken(string token, int userArgIndex)
    {
        if (token.StartsWith('[') && token.EndsWith(']'))
        {
            var inner = token[1..^1].Trim();
            if (inner.StartsWith('<') && inner.EndsWith('>'))
                inner = inner[1..^1].Trim();

            var kind = IsMessagePlaceholder(inner) ? CommandArgKind.Message : CommandArgKind.Text;
            return new CommandArgPart
            {
                Kind = kind,
                IsOptional = true,
                Placeholder = inner,
                Index = userArgIndex
            };
        }

        if (token.StartsWith('<') && token.EndsWith('>'))
        {
            var name = token[1..^1].Trim();
            return new CommandArgPart
            {
                Kind = IsMessagePlaceholder(name) ? CommandArgKind.Message : CommandArgKind.Text,
                Placeholder = name,
                Index = userArgIndex
            };
        }

        if (token.Contains('|', StringComparison.Ordinal))
        {
            return new CommandArgPart
            {
                Kind = CommandArgKind.Choice,
                Choices = token.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
                Index = userArgIndex
            };
        }

        return new CommandArgPart
        {
            Kind = CommandArgKind.Literal,
            Literal = token,
            Index = userArgIndex
        };
    }

    private static List<string> Tokenize(string syntax)
    {
        var tokens = new List<string>();
        var current = new System.Text.StringBuilder();
        var inWrapped = false;
        var wrapOpen = '\0';

        for (var i = 0; i < syntax.Length; i++)
        {
            var ch = syntax[i];

            if (!inWrapped && ch is '<' or '[')
            {
                FlushToken(tokens, current);
                inWrapped = true;
                wrapOpen = ch;
                current.Append(ch);
                continue;
            }

            if (inWrapped)
            {
                current.Append(ch);
                if ((ch == '>' && wrapOpen == '<') || (ch == ']' && wrapOpen == '['))
                    inWrapped = false;

                continue;
            }

            if (char.IsWhiteSpace(ch))
            {
                FlushToken(tokens, current);
                continue;
            }

            current.Append(ch);
        }

        FlushToken(tokens, current);
        return tokens;
    }

    private static void FlushToken(List<string> tokens, System.Text.StringBuilder current)
    {
        if (current.Length == 0)
            return;

        tokens.Add(current.ToString());
        current.Clear();
    }

    private static bool IsMessagePlaceholder(string name) =>
        MessagePlaceholderNames.Contains(name);

    private static bool TryConsumeLiteral(ref string remaining, string literal)
    {
        remaining = remaining.TrimStart();
        if (!remaining.StartsWith(literal, StringComparison.OrdinalIgnoreCase))
            return false;

        if (remaining.Length > literal.Length && remaining[literal.Length] != ' ')
            return false;

        remaining = remaining[literal.Length..].TrimStart();
        return true;
    }

    private static string? TryConsumeChoice(ref string remaining, IReadOnlyList<string> choices)
    {
        remaining = remaining.TrimStart();
        foreach (var choice in choices.OrderByDescending(c => c.Length))
        {
            if (!remaining.StartsWith(choice, StringComparison.OrdinalIgnoreCase))
                continue;

            if (remaining.Length > choice.Length && remaining[choice.Length] != ' ')
                continue;

            remaining = remaining[choice.Length..].TrimStart();
            return choice;
        }

        return null;
    }

    private static string? TryConsumeWord(ref string remaining)
    {
        remaining = remaining.TrimStart();
        if (remaining.Length == 0)
            return null;

        var space = remaining.IndexOf(' ');
        var word = space < 0 ? remaining : remaining[..space];
        remaining = space < 0 ? string.Empty : remaining[(space + 1)..];
        return word;
    }

    private static string NormalizeSyntax(string syntax)
    {
        syntax = syntax.Trim();
        return syntax.StartsWith('/') ? syntax[1..] : syntax;
    }
}
