namespace MineDash.Models;

public class ServerConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;

    public string Host { get; set; } = "localhost";
    public int RconPort { get; set; }
    public string RconPassword { get; set; } = string.Empty;

    /// <summary>
    /// Path to the Minecraft server log file (latest.log).
    /// For Docker containers, this should be the host path, e.g., /srv/minecraft/server-name/data/logs/latest.log
    /// </summary>
    public string? LogPath { get; set; }

    public string? Notes { get; set; }
}