using System.Text.Json;
using System.Text.Json.Serialization;
using MineDash.Models;

namespace MineDash.Services;

public class JsonServerConfigStore : IServerConfigStore
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public JsonServerConfigStore(IWebHostEnvironment env)
    {
        var dir = Path.Combine(env.ContentRootPath, "App_Data");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "servers.json");
    }

    public async Task<IReadOnlyList<ServerConfig>> GetAllAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (!File.Exists(_filePath))
                return Array.Empty<ServerConfig>();

            await using var stream = File.OpenRead(_filePath);
            var servers = await JsonSerializer.DeserializeAsync<List<ServerConfig>>(stream, _jsonOptions, ct)
                          ?? new List<ServerConfig>();

            return servers;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<ServerConfig?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        var all = await GetAllAsync(ct);
        return all.FirstOrDefault(s => s.Id == id);
    }

    public async Task AddOrUpdateAsync(ServerConfig server, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var servers = (await GetAllInternalAsync(ct)).ToList();

            var existing = servers.FirstOrDefault(s => s.Id == server.Id);
            if (existing is null)
            {
                if (string.IsNullOrWhiteSpace(server.Id))
                    server.Id = Guid.NewGuid().ToString("N");
                servers.Add(server);
            }
            else
            {
                var index = servers.IndexOf(existing);
                servers[index] = server;
            }

            await SaveInternalAsync(servers, ct);
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
            var servers = (await GetAllInternalAsync(ct))
                .Where(s => s.Id != id)
                .ToList();

            await SaveInternalAsync(servers, ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<IReadOnlyList<ServerConfig>> GetAllInternalAsync(CancellationToken ct)
    {
        if (!File.Exists(_filePath))
            return Array.Empty<ServerConfig>();

        await using var stream = File.OpenRead(_filePath);
        var servers = await JsonSerializer.DeserializeAsync<List<ServerConfig>>(stream, _jsonOptions, ct)
                      ?? new List<ServerConfig>();
        return servers;
    }

    private async Task SaveInternalAsync(IReadOnlyList<ServerConfig> servers, CancellationToken ct)
    {
        await using var stream = File.Create(_filePath);
        await JsonSerializer.SerializeAsync(stream, servers, _jsonOptions, ct);
    }
}