using System.Text.Json;
using System.Text.Json.Serialization;
using MineDash.Models;

namespace MineDash.Services;

public class JsonAppSettingsStore : IAppSettingsStore
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public JsonAppSettingsStore(IWebHostEnvironment env)
    {
        var dir = Path.Combine(env.ContentRootPath, "app_data");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "settings.json");
    }

    public async Task<AppSettings> GetAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (!File.Exists(_filePath))
            {
                // Return defaults if file doesn't exist
                return new AppSettings();
            }

            await using var stream = File.OpenRead(_filePath);
            var settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, _jsonOptions, ct)
                          ?? new AppSettings();
            return settings;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await using var stream = File.Create(_filePath);
            await JsonSerializer.SerializeAsync(stream, settings, _jsonOptions, ct);
        }
        finally
        {
            _lock.Release();
        }
    }
}

