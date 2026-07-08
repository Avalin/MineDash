namespace MineDash.Models;

public static class ConsoleAutoscrollPresets
{
    public static readonly (ConsoleAutoscrollMode Mode, string Label, string Description)[] Modes =
    [
        (ConsoleAutoscrollMode.Off, "Off", "Scroll only when you send a command (current behavior)"),
        (ConsoleAutoscrollMode.On, "On", "Always jump to the bottom when new lines appear"),
        (ConsoleAutoscrollMode.Auto, "Auto", "Follow the tail — scroll only if you are already at the bottom"),
    ];

    public static string GetShortLabel(ConsoleAutoscrollMode mode) =>
        Modes.FirstOrDefault(m => m.Mode == mode).Label ?? mode.ToString();
}
