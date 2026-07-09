using System.Text.Json;
using MineDash.Models;

namespace MineDash.Services;

public interface IServerUserCacheService
{
    Task<ServerUserCacheIndex> LoadAsync(ServerConfig server, CancellationToken ct = default);
}

public sealed class ServerUserCacheIndex
{
    public static ServerUserCacheIndex Empty { get; } = new([], [], []);

    private readonly Dictionary<string, string> _uuidToName;
    private readonly Dictionary<string, string> _nameToUuid;
    private readonly HashSet<string> _knownUuids;

    internal ServerUserCacheIndex(
        Dictionary<string, string> uuidToName,
        Dictionary<string, string> nameToUuid,
        HashSet<string> knownUuids)
    {
        _uuidToName = uuidToName;
        _nameToUuid = nameToUuid;
        _knownUuids = knownUuids;
    }

    public string? ResolveName(string uuid)
    {
        var normalized = NormalizeUuid(uuid);
        return _uuidToName.TryGetValue(normalized, out var name) ? name : null;
    }

    public string? ResolveUuid(string name) =>
        _nameToUuid.TryGetValue(name.Trim(), out var uuid) ? uuid : null;

    public bool HasJoined(string uuid) =>
        _knownUuids.Contains(NormalizeUuid(uuid));

    public bool HasJoinedName(string name) =>
        _nameToUuid.ContainsKey(name.Trim());

    public static string NormalizeUuid(string uuid) =>
        uuid.Replace("-", string.Empty, StringComparison.Ordinal);

    internal static ServerUserCacheIndex FromEntries(IEnumerable<UserCacheFileEntry> entries)
    {
        var uuidToName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var nameToUuid = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var knownUuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Uuid))
                continue;

            var uuid = NormalizeUuid(entry.Uuid);
            knownUuids.Add(uuid);

            if (string.IsNullOrWhiteSpace(entry.Name))
                continue;

            var name = entry.Name.Trim();
            uuidToName[uuid] = name;
            nameToUuid[name] = uuid;
        }

        return new ServerUserCacheIndex(uuidToName, nameToUuid, knownUuids);
    }

    internal sealed class UserCacheFileEntry
    {
        public string Name { get; set; } = string.Empty;
        public string Uuid { get; set; } = string.Empty;
    }
}

public sealed class ServerUserCacheService : IServerUserCacheService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<ServerUserCacheIndex> LoadAsync(ServerConfig server, CancellationToken ct = default)
    {
        foreach (var path in ServerPathResolver.GetUserCacheCandidates(server))
        {
            ct.ThrowIfCancellationRequested();
            if (!File.Exists(path))
                continue;

            try
            {
                await using var stream = File.OpenRead(path);
                var entries = await JsonSerializer.DeserializeAsync<List<ServerUserCacheIndex.UserCacheFileEntry>>(
                    stream, JsonOptions, ct);

                if (entries is null || entries.Count == 0)
                    continue;

                return ServerUserCacheIndex.FromEntries(entries);
            }
            catch
            {
                // Try the next candidate path.
            }
        }

        return ServerUserCacheIndex.Empty;
    }
}
