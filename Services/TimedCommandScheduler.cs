using MineDash.Models;

namespace MineDash.Services;

public class TimedCommandScheduler : BackgroundService, ITimedCommandScheduler
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TimedCommandScheduler> _logger;

    public TimedCommandScheduler(
        IServiceProvider serviceProvider,
        ILogger<TimedCommandScheduler> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Timed Command Scheduler started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckAndExecuteCommandsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in timed command scheduler");
            }

            // Check every minute
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }

        _logger.LogInformation("Timed Command Scheduler stopped");
    }

    private async Task CheckAndExecuteCommandsAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var commandStore = scope.ServiceProvider.GetRequiredService<ITimedCommandStore>();
        var serverStore = scope.ServiceProvider.GetRequiredService<IServerConfigStore>();
        var rconService = scope.ServiceProvider.GetRequiredService<IRconService>();

        var commands = await commandStore.GetAllAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var currentMinute = now.Minute;
        var currentHour = now.Hour;
        var currentWeekday = (int)now.DayOfWeek; // Sunday = 0, Monday = 1, etc.

        foreach (var command in commands)
        {
            if (!command.IsActive)
                continue;

            // Check if this command should run now
            if (ShouldRunNow(command, currentMinute, currentHour, currentWeekday))
            {
                try
                {
                    var server = await serverStore.GetByIdAsync(command.ServerId, cancellationToken);
                    if (server == null)
                    {
                        _logger.LogWarning("Server {ServerId} not found for timed command {CommandId}", 
                            command.ServerId, command.Id);
                        continue;
                    }

                    _logger.LogInformation("Executing timed command '{CommandName}' on server '{ServerName}'", 
                        command.Name, server.Name);

                    var response = await rconService.SendCommandAsync(server, command.Command, cancellationToken);
                    
                    _logger.LogInformation("Timed command '{CommandName}' executed successfully. Response: {Response}", 
                        command.Name, response);

                    // Update last run time and calculate next run time
                    command.LastRunAt = now;
                    command.NextRunAt = CalculateNextRunTime(command, now);
                    await commandStore.AddOrUpdateAsync(command, cancellationToken);
                }
                catch (Exception ex)
                {
                    // Log connection failures as warnings (expected when server is down)
                    // Log other errors as errors
                    var isConnectionError = ex.Message.Contains("Failed to connect", StringComparison.OrdinalIgnoreCase) ||
                                           ex.Message.Contains("socket", StringComparison.OrdinalIgnoreCase) ||
                                           ex.Message.Contains("not connected", StringComparison.OrdinalIgnoreCase);
                    
                    if (isConnectionError)
                    {
                        _logger.LogWarning("Timed command '{CommandName}' skipped - server unavailable: {Error}", 
                            command.Name, ex.Message);
                    }
                    else
                    {
                        _logger.LogError(ex, "Failed to execute timed command '{CommandName}'", command.Name);
                    }
                    
                    // Still update next run time to avoid retrying immediately
                    // This prevents spam when server is down
                    command.NextRunAt = CalculateNextRunTime(command, now);
                    await commandStore.AddOrUpdateAsync(command, cancellationToken);
                }
            }
            else
            {
                // Update next run time if not set or if it's in the past
                if (command.NextRunAt == null || command.NextRunAt < now)
                {
                    command.NextRunAt = CalculateNextRunTime(command, now);
                    await commandStore.AddOrUpdateAsync(command, cancellationToken);
                }
            }
        }
    }

    private bool ShouldRunNow(TimedCommand command, int currentMinute, int currentHour, int currentWeekday)
    {
        // Check if current minute matches
        if (command.Minutes.Count > 0 && !command.Minutes.Contains(currentMinute))
            return false;

        // Check if current hour matches
        if (command.Hours.Count > 0 && !command.Hours.Contains(currentHour))
            return false;

        // Check if current weekday matches
        if (command.Weekdays.Count > 0 && !command.Weekdays.Contains(currentWeekday))
            return false;

        return true;
    }

    private DateTime CalculateNextRunTime(TimedCommand command, DateTime fromTime)
    {
        // Start from the next minute
        var next = fromTime.AddMinutes(1);
        next = new DateTime(next.Year, next.Month, next.Day, next.Hour, next.Minute, 0, DateTimeKind.Utc);

        // Try to find the next valid time within the next 7 days
        for (int dayOffset = 0; dayOffset < 7; dayOffset++)
        {
            var candidate = next.AddDays(dayOffset);
            var candidateWeekday = (int)candidate.DayOfWeek;

            // Check if weekday matches
            if (command.Weekdays.Count > 0 && !command.Weekdays.Contains(candidateWeekday))
                continue;

            // Try each hour
            for (int hour = dayOffset == 0 ? candidate.Hour : 0; hour < 24; hour++)
            {
                candidate = new DateTime(candidate.Year, candidate.Month, candidate.Day, hour, 0, 0, DateTimeKind.Utc);

                // Check if hour matches
                if (command.Hours.Count > 0 && !command.Hours.Contains(hour))
                    continue;

                // Try each minute
                for (int minute = (dayOffset == 0 && hour == candidate.Hour) ? candidate.Minute : 0; minute < 60; minute++)
                {
                    candidate = new DateTime(candidate.Year, candidate.Month, candidate.Day, hour, minute, 0, DateTimeKind.Utc);

                    // Check if minute matches
                    if (command.Minutes.Count > 0 && !command.Minutes.Contains(minute))
                        continue;

                    // Found a valid time
                    if (candidate > fromTime)
                        return candidate;
                }
            }
        }

        // If no valid time found in 7 days, return a week from now (shouldn't happen in practice)
        return fromTime.AddDays(7);
    }
}

