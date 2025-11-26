using System.Text.Json;
using System.Text.Json.Serialization;
using MineDash.Models;

namespace MineDash.Services;

public class JsonCommandStore : ICommandStore
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public JsonCommandStore(IWebHostEnvironment env)
    {
        var dir = Path.Combine(env.ContentRootPath, "app_data");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "commands.json");
    }

    public async Task<IReadOnlyList<MinecraftCommand>> GetAllAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (!File.Exists(_filePath))
            {
                // Initialize with defaults if file doesn't exist
                await ResetToDefaultsInternalAsync(ct);
                return await GetAllInternalAsync(ct);
            }

            return await GetAllInternalAsync(ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<MinecraftCommand?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        var all = await GetAllAsync(ct);
        return all.FirstOrDefault(c => c.Id == id);
    }

    public async Task AddOrUpdateAsync(MinecraftCommand command, CancellationToken ct = default)
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

    public async Task ResetToDefaultsAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await ResetToDefaultsInternalAsync(ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task ResetToDefaultsInternalAsync(CancellationToken ct)
    {
        var defaults = new List<MinecraftCommand>
        {
            new() { Id = Guid.NewGuid().ToString("N"), Syntax = "/say <message>", Description = "Broadcast a message" },
            new() { Id = Guid.NewGuid().ToString("N"), Syntax = "/list", Description = "List players" },
            new() { Id = Guid.NewGuid().ToString("N"), Syntax = "/kick <player> [<reason>]", Description = "" },
            new() { Id = Guid.NewGuid().ToString("N"), Syntax = "/ban <player> [<reason>]", Description = "" },
            new() { Id = Guid.NewGuid().ToString("N"), Syntax = "/time set day|night|noon|midnight", Description = "" },
            new() { Id = Guid.NewGuid().ToString("N"), Syntax = "/gamemode survival|creative|adventure|spectator [player]", Description = "" },
            new() { Id = Guid.NewGuid().ToString("N"), Syntax = "/tp <target> <destination>", Description = "" },
            new() { Id = Guid.NewGuid().ToString("N"), Syntax = "/stop", Description = "Stop the server" }
        };

        await SaveInternalAsync(defaults, ct);
    }

    private async Task<IReadOnlyList<MinecraftCommand>> GetAllInternalAsync(CancellationToken ct)
    {
        if (!File.Exists(_filePath))
            return Array.Empty<MinecraftCommand>();

        await using var stream = File.OpenRead(_filePath);
        var commands = await JsonSerializer.DeserializeAsync<List<MinecraftCommand>>(stream, _jsonOptions, ct)
                      ?? new List<MinecraftCommand>();
        return commands;
    }

    private async Task SaveInternalAsync(IReadOnlyList<MinecraftCommand> commands, CancellationToken ct)
    {
        await using var stream = File.Create(_filePath);
        await JsonSerializer.SerializeAsync(stream, commands, _jsonOptions, ct);
    }
}

