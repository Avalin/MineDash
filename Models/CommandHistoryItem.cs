namespace MineDash.Models;

public class CommandHistoryItem
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string Command { get; set; } = string.Empty;
    public string Response { get; set; } = string.Empty;
}
