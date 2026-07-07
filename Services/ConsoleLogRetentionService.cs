using MineDash.Models;

namespace MineDash.Services;

public interface IConsoleLogRetentionService
{
    DateTime? GetCutoffUtc(ConsoleState state);
    bool IsWithinWindow(ConsoleState state, DateTime timestampUtc);
    int GetInitialTailLines(ConsoleState state);
    void ApplyRetention(ConsoleState state);
    void NormalizeLimits(ConsoleState state);
    string FormatTimeLabel(int minutes);
    string FormatLineLabel(int lineLimit);
    string FormatTimeShort(int minutes);
    string FormatLineShort(int lineLimit);
}

public sealed class ConsoleLogRetentionService : IConsoleLogRetentionService
{
    public DateTime? GetCutoffUtc(ConsoleState state)
    {
        if (state.LogHistoryMinutes <= 0)
            return null;

        return DateTime.UtcNow.AddMinutes(-state.LogHistoryMinutes);
    }

    public bool IsWithinWindow(ConsoleState state, DateTime timestampUtc)
    {
        var cutoff = GetCutoffUtc(state);
        return cutoff is null || timestampUtc >= cutoff.Value;
    }

    public int GetInitialTailLines(ConsoleState state)
    {
        NormalizeLimits(state);

        if (state.LogLineLimit <= 0)
            return LogHistoryPresets.MaxReadLines;

        return Math.Min(state.LogLineLimit, LogHistoryPresets.MaxReadLines);
    }

    public void NormalizeLimits(ConsoleState state)
    {
        if (!LogHistoryPresets.Time.Any(p => p.Minutes == state.LogHistoryMinutes))
            state.LogHistoryMinutes = LogHistoryPresets.DefaultMinutes;

        if (state.LogLineLimit > 0)
        {
            state.LogLineLimit = Math.Clamp(
                state.LogLineLimit,
                LogHistoryPresets.MinLineLimit,
                LogHistoryPresets.MaxReadLines);
        }
        else if (!LogHistoryPresets.Lines.Any(p => p.Lines == state.LogLineLimit))
        {
            state.LogLineLimit = LogHistoryPresets.DefaultLineLimit;
        }
    }

    public void ApplyRetention(ConsoleState state)
    {
        NormalizeLimits(state);

        if (state.LiveLogs.Count == 0)
            return;

        var cutoff = GetCutoffUtc(state);
        if (cutoff is not null)
        {
            for (var i = state.LiveLogs.Count - 1; i >= 0; i--)
            {
                if (state.LiveLogs[i].Timestamp < cutoff.Value)
                    state.LiveLogs.RemoveAt(i);
            }
        }

        if (state.LogLineLimit > 0 && state.LiveLogs.Count > state.LogLineLimit)
            state.LiveLogs.RemoveRange(0, state.LiveLogs.Count - state.LogLineLimit);
    }

    public string FormatTimeLabel(int minutes) =>
        LogHistoryPresets.Time.FirstOrDefault(p => p.Minutes == minutes).Label ?? FormatDuration(minutes);

    public string FormatLineLabel(int lineLimit) =>
        lineLimit <= 0
            ? "All lines"
            : LogHistoryPresets.Lines.FirstOrDefault(p => p.Lines == lineLimit).Label ?? lineLimit.ToString("N0");

    public string FormatTimeShort(int minutes) => minutes switch
    {
        0 => "All",
        30 => "30m",
        60 => "1h",
        360 => "6h",
        720 => "12h",
        1_440 => "24h",
        2_880 => "48h",
        4_320 => "72h",
        10_080 => "7d",
        _ => FormatDuration(minutes)
    };

    public string FormatLineShort(int lineLimit) => lineLimit switch
    {
        0 => "All",
        500 => "500",
        1_000 => "1k",
        5_000 => "5k",
        10_000 => "10k",
        25_000 => "25k",
        50_000 => "50k",
        _ => lineLimit.ToString("N0")
    };

    private static string FormatDuration(int minutes)
    {
        if (minutes < 60)
            return $"{minutes}m";

        if (minutes % 1_440 == 0)
            return $"{minutes / 1_440}d";

        return $"{minutes / 60}h";
    }
}
