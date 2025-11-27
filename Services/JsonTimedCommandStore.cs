using System.Text.Json;
using System.Text.Json.Serialization;
using MineDash.Models;

namespace MineDash.Services;

public class JsonTimedCommandStore : ITimedCommandStore
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public JsonTimedCommandStore(IWebHostEnvironment env)
    {
        var dir = Path.Combine(env.ContentRootPath, "app_data");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "timed-commands.json");
    }

    public async Task<IReadOnlyList<TimedCommand>> GetAllAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            return await GetAllInternalAsync(ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<TimedCommand?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        var all = await GetAllAsync(ct);
        return all.FirstOrDefault(c => c.Id == id);
    }

    public async Task AddOrUpdateAsync(TimedCommand command, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var commands = (await GetAllInternalAsync(ct)).ToList();

            var existing = commands.FirstOrDefault(c => c.Id == command.Id);
            if (existing is null)
            {
                if (string.IsNullOrWhiteSpace(command.Id))
                    command.Id = Guid.NewGuid().ToString("N");
                commands.Add(command);
            }
            else
            {
                var index = commands.IndexOf(existing);
                commands[index] = command;
            }

            await SaveInternalAsync(commands, ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var commands = (await GetAllInternalAsync(ct))
                .Where(c => c.Id != id)
                .ToList();

            await SaveInternalAsync(commands, ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<IReadOnlyList<TimedCommand>> GetAllInternalAsync(CancellationToken ct)
    {
        if (!File.Exists(_filePath))
            return Array.Empty<TimedCommand>();

        await using var stream = File.OpenRead(_filePath);
        var commands = await JsonSerializer.DeserializeAsync<List<TimedCommand>>(stream, _jsonOptions, ct)
                      ?? new List<TimedCommand>();
        return commands;
    }

    private async Task SaveInternalAsync(IReadOnlyList<TimedCommand> commands, CancellationToken ct)
    {
        await using var stream = File.Create(_filePath);
        await JsonSerializer.SerializeAsync(stream, commands, _jsonOptions, ct);
    }
}

