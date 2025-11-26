namespace MineDash.Models;

public class MinecraftCommand
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Syntax { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

