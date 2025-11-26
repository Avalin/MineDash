namespace MineDash.Models;

public class ServerConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;

    public string Host { get; set; } = "localhost";
    public int RconPort { get; set; }
    public string RconPassword { get; set; } = string.Empty;

    public string? Notes { get; set; }
}