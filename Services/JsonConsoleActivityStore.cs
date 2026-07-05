using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using MineDash.Models;

namespace MineDash.Services;

public class JsonConsoleActivityStore : IConsoleActivityStore
{
    private const int MaxEntriesPerServer = 200;

    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly ConcurrentDictionary<string, List<CommandHistoryItem>> _history = new();
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public event Action<string>? HistoryChanged;

    public JsonConsoleActivityStore(IWebHostEnvironment env)
    {
        var dir = Path.Combine(env.ContentRootPath, "app_data");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "console_activity.json");
        LoadFromDisk();
    }

    public IReadOnlyList<CommandHistoryItem> GetHistory(string serverId)
    {
        return _history.TryGetValue(serverId, out var items)
            ? items.ToList()
            : Array.Empty<CommandHistoryItem>();
    }

    public async Task AppendAsync(string serverId, CommandHistoryItem item, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var items = _history.GetOrAdd(serverId, _ => new List<CommandHistoryItem>());
            items.Add(item);

            if (items.Count > MaxEntriesPerServer)
                items.RemoveRange(0, items.Count - MaxEntriesPerServer);

            await SaveInternalAsync(ct);
        }
        finally
        {
            _lock.Release();
        }

        HistoryChanged?.Invoke(serverId);
    }

    private void LoadFromDisk()
    {
        if (!File.Exists(_filePath))
            return;

        try
        {
            var json = File.ReadAllText(_filePath);
            var data = JsonSerializer.Deserialize<Dictionary<string, List<CommandHistoryItem>>>(
                json, _jsonOptions) ?? new Dictionary<string, List<CommandHistoryItem>>();

            foreach (var (serverId, items) in data)
                _history[serverId] = items;
        }
        catch
        {
            // Start fresh if the file is corrupt
        }
    }

    private async Task SaveInternalAsync(CancellationToken ct = default)
    {
        var snapshot = _history.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        await using var stream = File.Create(_filePath);
        await JsonSerializer.SerializeAsync(stream, snapshot, _jsonOptions, ct);
    }
}
