namespace MineDash.Models;

public class ServerConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;

    public string Host { get; set; } = "localhost";
    public int RconPort { get; set; }
    public string RconPassword { get; set; } = string.Empty;

    /// <summary>
    /// Path to the Minecraft server folder as seen inside the MineDash container.
    /// Example: /srv/minecraft/creatamon
    /// Older configurations may still contain a direct latest.log path.
    /// </summary>
    public string? LogPath { get; set; }

    /// <summary>
    /// Timezone used in latest.log timestamps. Docker Minecraft servers usually log in UTC.
    /// </summary>
    public string? LogTimeZoneId { get; set; }

    public string? Notes { get; set; }
}