namespace MineDash.Models;

public enum ServerConfigSource
{
    Manual,
    DockerCompose
}

public class ServerConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;

    public ServerConfigSource ConfigSource { get; set; } = ServerConfigSource.Manual;

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

    public string? ComposeFilePath { get; set; }
    public string? ComposeServiceName { get; set; }
    public string? ComposeContainerName { get; set; }
    public string? ComposeImage { get; set; }
    public string? ComposeVersion { get; set; }
    public string? ComposeDataVolumeSource { get; set; }
    public string? ComposeDataVolumeTarget { get; set; }
    public string? ComposeMemory { get; set; }
}