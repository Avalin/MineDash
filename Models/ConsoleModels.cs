namespace MineDash.Models;

using MineDash.Services;

public sealed class ConsoleState
{
    public ConsoleState(string serverId)
    {
        ServerId = serverId;
        ShowLogs = true;
        ShowCommands = true;
        SelectedLogLevels = new HashSet<string>();
        SelectedThreads = new HashSet<string>();
    }

    public string ServerId { get; }
    public string SelectedCommandId { get; set; } = string.Empty;
    public string CommandArguments { get; set; } = string.Empty;
    public List<string> CommandHistoryOnly { get; } = new();
    public int HistoryIndex { get; set; } = -1;
    public bool ShowLogs { get; set; }
    public bool ShowCommands { get; set; }
    public List<LogEntry> LiveLogs { get; } = new();
    public long LogFilePosition { get; set; }
    public DateTime LastLogPoll { get; set; } = DateTime.MinValue;
    public HashSet<string> SelectedLogLevels { get; set; }
    public HashSet<string> SelectedThreads { get; set; }
    public bool LevelFilterActive { get; set; }
    public bool ThreadFilterActive { get; set; }
    public string? OpenFilterDropdown { get; set; }
}

public sealed class ConsoleMergedEntry
{
    public DateTime Timestamp { get; set; }
    public int Sequence { get; set; }
    public bool IsLog { get; set; }
    public string? LogLine { get; set; }
    public string? Command { get; set; }
    public string? Response { get; set; }
    public string? ExecutedBy { get; set; }
}

public sealed class HomeConsolePersistedState
{
    public List<string>? OpenConsoleIds { get; set; }
    public int LayoutColumns { get; set; } = 1;
    public Dictionary<string, ConsoleToggleState>? ConsoleStates { get; set; }
}

public sealed class ConsoleToggleState
{
    public bool ShowLogs { get; set; } = true;
    public bool ShowCommands { get; set; } = true;
    public bool LevelFilterActive { get; set; }
    public bool ThreadFilterActive { get; set; }
    public List<string>? SelectedLogLevels { get; set; }
    public List<string>? SelectedThreads { get; set; }
}
