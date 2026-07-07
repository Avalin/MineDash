using System.Text.RegularExpressions;

namespace MineDash.Services;

/// <summary>
/// Resolves log line CSS classes from level, thread, and conservative exception heuristics.
/// </summary>
public static class ConsoleLogAppearance
{
    private static readonly Regex QualifiedThrowableRegex = new(
        @"\b[a-z][\w$]*(?:\.[a-z][\w$]*)+\.(?:\w+Exception|\w+Error)\b",
        RegexOptions.Compiled);

    private static readonly Regex LeadingThrowableLineRegex = new(
        @"^(?:\w+\.)+\w+(?:Exception|Error):\s",
        RegexOptions.Compiled);

    private static readonly Regex InternalExceptionRegex = new(
        @"Internal Exception:\s*\S",
        RegexOptions.Compiled);

    private static readonly Regex StackTraceAtRegex = new(
        @"^\s+at\s+\S+\(",
        RegexOptions.Compiled);

    private static readonly Regex CausedByRegex = new(
        @"^\s*Caused by:\s",
        RegexOptions.Compiled);

    public static string GetMessageClasses(string? levelKey, string? threadKey, string? message)
    {
        var level = levelKey ?? string.Empty;

        if (IsErrorLevel(level))
            return "log-severity-error";

        if (IsWarnLevel(level))
            return "log-severity-warn";

        if (LooksLikeThrowable(message, threadKey))
            return "log-severity-error";

        return ConsoleThreadNormalizer.GetMessageColorClass(threadKey ?? string.Empty) ?? string.Empty;
    }

    public static bool LooksLikeThrowable(string? message, string? threadKey)
    {
        if (string.IsNullOrWhiteSpace(message))
            return false;

        if (IsChatThread(threadKey))
            return false;

        var trimmed = message.TrimStart();

        if (InternalExceptionRegex.IsMatch(message))
            return true;

        if (LeadingThrowableLineRegex.IsMatch(trimmed))
            return true;

        if (CausedByRegex.IsMatch(trimmed))
            return true;

        if (StackTraceAtRegex.IsMatch(trimmed))
            return true;

        return QualifiedThrowableRegex.IsMatch(message);
    }

    private static bool IsChatThread(string? threadKey) =>
        threadKey?.Equals("Async Chat Thread", StringComparison.OrdinalIgnoreCase) == true;

    private static bool IsErrorLevel(string level) =>
        level.Equals("ERROR", StringComparison.OrdinalIgnoreCase)
        || level.Equals("FATAL", StringComparison.OrdinalIgnoreCase);

    private static bool IsWarnLevel(string level) =>
        level.Equals("WARN", StringComparison.OrdinalIgnoreCase);
}
