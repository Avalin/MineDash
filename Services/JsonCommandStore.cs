using System.Text.Json;
using System.Text.Json.Serialization;
using MineDash.Models;

namespace MineDash.Services;

public class JsonCommandStore : ICommandStore
{
    private readonly string _usersDir;
    private readonly IAuthService _authService;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public JsonCommandStore(IWebHostEnvironment env, IAuthService authService)
    {
        _authService = authService;
        _usersDir = Path.Combine(env.ContentRootPath, "app_data", "users");
        Directory.CreateDirectory(_usersDir);
    }

    public async Task<IReadOnlyList<MinecraftCommand>> GetAllAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var filePath = await GetUserFilePathAsync(ct);
            if (!File.Exists(filePath))
            {
                await ResetToDefaultsInternalAsync(filePath, ct);
                return await GetAllInternalAsync(filePath, ct);
            }

            return await GetAllInternalAsync(filePath, ct);
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
            var filePath = await GetUserFilePathAsync(ct);
            var commands = (await GetAllInternalAsync(filePath, ct)).ToList();

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

            await SaveInternalAsync(filePath, commands, ct);
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
            var filePath = await GetUserFilePathAsync(ct);
            var commands = (await GetAllInternalAsync(filePath, ct))
                .Where(c => c.Id != id)
                .ToList();

            await SaveInternalAsync(filePath, commands, ct);
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
            var filePath = await GetUserFilePathAsync(ct);
            await ResetToDefaultsInternalAsync(filePath, ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<string> GetUserFilePathAsync(CancellationToken ct)
    {
        var user = await _authService.GetCurrentUserAsync();
        if (user is null)
            throw new InvalidOperationException("Commands are only available for authenticated users.");

        var userDir = Path.Combine(_usersDir, user.Id);
        Directory.CreateDirectory(userDir);
        return Path.Combine(userDir, "commands.json");
    }

    private static async Task ResetToDefaultsInternalAsync(string filePath, CancellationToken ct)
    {
        await SaveInternalAsync(filePath, CreateDefaultCommands(), ct);
    }

    private static List<MinecraftCommand> CreateDefaultCommands() =>
    [
        new() { Id = Guid.NewGuid().ToString("N"), Syntax = "/say <message>", Description = "Broadcast a message" },
        new() { Id = Guid.NewGuid().ToString("N"), Syntax = "/list", Description = "List players" },
        new() { Id = Guid.NewGuid().ToString("N"), Syntax = "/kick <player> [<reason>]", Description = "" },
        new() { Id = Guid.NewGuid().ToString("N"), Syntax = "/ban <player> [<reason>]", Description = "" },
        new() { Id = Guid.NewGuid().ToString("N"), Syntax = "/op <player>", Description = "" },
        new() { Id = Guid.NewGuid().ToString("N"), Syntax = "/deop <player>", Description = "" },
        new() { Id = Guid.NewGuid().ToString("N"), Syntax = "/time set day|night|noon|midnight", Description = "" },
        new() { Id = Guid.NewGuid().ToString("N"), Syntax = "/gamemode survival|creative|adventure|spectator [player]", Description = "" },
        new() { Id = Guid.NewGuid().ToString("N"), Syntax = "/tp <target> <destination>", Description = "" },
        new() { Id = Guid.NewGuid().ToString("N"), Syntax = "/stop", Description = "Stop the server" }
    ];

    private static async Task<IReadOnlyList<MinecraftCommand>> GetAllInternalAsync(string filePath, CancellationToken ct)
    {
        if (!File.Exists(filePath))
            return Array.Empty<MinecraftCommand>();

        await using var stream = File.OpenRead(filePath);
        var commands = await JsonSerializer.DeserializeAsync<List<MinecraftCommand>>(stream, JsonOptions, ct)
                      ?? new List<MinecraftCommand>();
        return commands;
    }

    private static async Task SaveInternalAsync(string filePath, IReadOnlyList<MinecraftCommand> commands, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        await using var stream = File.Create(filePath);
        await JsonSerializer.SerializeAsync(stream, commands, JsonOptions, ct);
    }
}
