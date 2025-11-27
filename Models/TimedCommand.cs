namespace MineDash.Models;

public class TimedCommand
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public string ServerId { get; set; } = string.Empty;
    
    // Scheduling fields
    public List<int> Minutes { get; set; } = new(); // 0-59
    public List<int> Hours { get; set; } = new(); // 0-23
    public List<int> Weekdays { get; set; } = new(); // 0-6 (Sunday=0, Monday=1, ..., Saturday=6)
    
    public bool IsActive { get; set; } = true;
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastRunAt { get; set; }
    public DateTime? NextRunAt { get; set; }
    
    // Failure tracking for auto-disable
    public DateTime? FirstFailureAt { get; set; }
    public int ConsecutiveFailures { get; set; }
    public DateTime? AutoDisabledAt { get; set; }
}

