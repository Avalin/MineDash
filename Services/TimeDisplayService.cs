namespace MineDash.Services;

public class TimeDisplayService : ITimeDisplayService
{
    private readonly TimeZoneInfo _displayTimeZone;

    public TimeDisplayService(IConfiguration configuration)
    {
        var tzId = configuration["MineDash:DisplayTimeZoneId"];
        _displayTimeZone = ResolveTimeZone(
            string.IsNullOrWhiteSpace(tzId) ? TimeZoneInfo.Local.Id : tzId);
    }

    public string FormatTime(DateTime timestamp)
    {
        var utc = ToUtc(timestamp);
        var display = TimeZoneInfo.ConvertTimeFromUtc(utc, _displayTimeZone);
        return display.ToString("HH:mm:ss");
    }

    public DateTime NormalizeForSort(DateTime timestamp) => ToUtc(timestamp);

    private static DateTime ToUtc(DateTime timestamp) =>
        timestamp.Kind switch
        {
            DateTimeKind.Utc => timestamp,
            DateTimeKind.Local => timestamp.ToUniversalTime(),
            _ => DateTime.SpecifyKind(timestamp, DateTimeKind.Utc)
        };

    internal static TimeZoneInfo ResolveTimeZone(string timeZoneId)
    {
        foreach (var id in new[] { timeZoneId, MapCommonIds(timeZoneId) })
        {
            if (string.IsNullOrWhiteSpace(id))
                continue;

            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return TimeZoneInfo.Utc;
    }

    private static string? MapCommonIds(string timeZoneId) =>
        timeZoneId switch
        {
            "Europe/Oslo" => "W. Europe Standard Time",
            "Europe/Stockholm" => "W. Europe Standard Time",
            "Europe/Copenhagen" => "W. Europe Standard Time",
            "Europe/Berlin" => "W. Europe Standard Time",
            "W. Europe Standard Time" => "Europe/Oslo",
            _ => null
        };
}
