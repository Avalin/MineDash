namespace MineDash.Models;

public static class LogHistoryPresets
{
    public static readonly LogHistoryPreset[] Time =
    [
        new(30, "30 minutes"),
        new(60, "1 hour"),
        new(360, "6 hours"),
        new(720, "12 hours"),
        new(1_440, "24 hours"),
        new(2_880, "48 hours"),
        new(4_320, "72 hours"),
        new(10_080, "7 days"),
        new(0, "All time")
    ];

    public static readonly LogLinePreset[] Lines =
    [
        new(500, "500"),
        new(1_000, "1,000"),
        new(5_000, "5,000"),
        new(10_000, "10,000"),
        new(25_000, "25,000"),
        new(50_000, "50,000"),
        new(0, "All lines")
    ];

    public const int DefaultMinutes = 60;
    public const int DefaultLineLimit = 10_000;
    public const int MaxReadLines = 50_000;
    public const int MinLineLimit = 100;
}

public readonly record struct LogHistoryPreset(int Minutes, string Label);
public readonly record struct LogLinePreset(int Lines, string Label);
