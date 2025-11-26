using System.Text.Json;
using System.Text.Json.Serialization;
using MineDash.Models;

namespace MineDash.Services;

public class JsonUserStore : IUserStore
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public JsonUserStore(IWebHostEnvironment env)
    {
        var dir = Path.Combine(env.ContentRootPath, "app_data");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "users.json");
    }

    public async Task<IReadOnlyList<User>> GetAllAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (!File.Exists(_filePath))
                return Array.Empty<User>();

            await using var stream = File.OpenRead(_filePath);
            var users = await JsonSerializer.DeserializeAsync<List<User>>(stream, _jsonOptions, ct)
                          ?? new List<User>();

            return users;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<User?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        var all = await GetAllAsync(ct);
        return all.FirstOrDefault(u => u.Id == id);
    }

    public async Task<User?> GetByUsernameAsync(string username, CancellationToken ct = default)
    {
        var all = await GetAllAsync(ct);
        var trimmedUsername = username?.Trim() ?? string.Empty;
        return all.FirstOrDefault(u => u.Username.Equals(trimmedUsername, StringComparison.OrdinalIgnoreCase));
    }

    public async Task AddOrUpdateAsync(User user, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var users = (await GetAllInternalAsync(ct)).ToList();

            var existing = users.FirstOrDefault(u => u.Id == user.Id);
            if (existing is null)
            {
                // This is a new user
                if (string.IsNullOrWhiteSpace(user.Id))
                    user.Id = Guid.NewGuid().ToString("N");
                
                // Check if this is the first user - make them admin
                if (users.Count == 0)
                {
                    user.IsAdmin = true;
                }
                
                users.Add(user);
            }
            else
            {
                var index = users.IndexOf(existing);
                users[index] = user;
            }

            await SaveInternalAsync(users, ct);
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
            var users = (await GetAllInternalAsync(ct))
                .Where(u => u.Id != id)
                .ToList();

            await SaveInternalAsync(users, ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<IReadOnlyList<User>> GetAllInternalAsync(CancellationToken ct)
    {
        if (!File.Exists(_filePath))
            return Array.Empty<User>();

        await using var stream = File.OpenRead(_filePath);
        var users = await JsonSerializer.DeserializeAsync<List<User>>(stream, _jsonOptions, ct)
                      ?? new List<User>();
        return users;
    }

    private async Task SaveInternalAsync(IReadOnlyList<User> users, CancellationToken ct)
    {
        await using var stream = File.Create(_filePath);
        await JsonSerializer.SerializeAsync(stream, users, _jsonOptions, ct);
    }
}

