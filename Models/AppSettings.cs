namespace MineDash.Models;

public class AppSettings
{
    public int AutoDisableTimeoutMinutes { get; set; } = 10; // Default 10 minutes, 0 = disabled

    /// <summary>
    /// When true, MineDash looks up player skin faces (cached locally after the first fetch).
    /// </summary>
    public bool EnablePlayerAvatars { get; set; } = true;
}

