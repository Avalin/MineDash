using System.Text.Json;
using MineDash.Models;

namespace MineDash.Services;

public interface IServerAccessListService
{
    Task<IReadOnlyList<ServerProfileEntry>> LoadWhitelistAsync(ServerConfig server, CancellationToken ct = default);
    Task<IReadOnlyList<ServerBanEntry>> LoadBansAsync(ServerConfig server, CancellationToken ct = default);
}

public sealed class ServerProfileEntry
{
    public required string Uuid { get; init; }
    public string Name { get; init; } = string.Empty;
}

public sealed class ServerBanEntry
{
    public required string Uuid { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Reason { get; init; }
}

public sealed class ServerAccessListService : IServerAccessListService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<IReadOnlyList<ServerProfileEntry>> LoadWhitelistAsync(
        ServerConfig server,
        CancellationToken ct = default) =>
        await LoadProfileListAsync(server, "whitelist.json", ct);

    public async Task<IReadOnlyList<ServerBanEntry>> LoadBansAsync(
        ServerConfig server,
        CancellationToken ct = default)
    {
        foreach (var path in ServerPathResolver.GetDataFolderFileCandidates(server.LogPath, "banned-players.json")
                     .Concat(ServerPathResolver.GetDataFolderFileCandidates(server.ComposeDataVolumeSource, "banned-players.json")))
        {
            ct.ThrowIfCancellationRequested();
            if (!File.Exists(path))
                continue;

            try
            {
                await using var stream = File.OpenRead(path);
                var entries = await JsonSerializer.DeserializeAsync<List<BanFileEntry>>(stream, JsonOptions, ct);
                if (entries is null)
                    continue;

                return entries
                    .Where(e => !string.IsNullOrWhiteSpace(e.Uuid))
                    .Select(e => new ServerBanEntry
                    {
                        Uuid = ServerUserCacheIndex.NormalizeUuid(e.Uuid),
                        Name = e.Name?.Trim() ?? string.Empty,
                        Reason = string.IsNullOrWhiteSpace(e.Reason) ? null : e.Reason.Trim()
                    })
                    .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch
            {
                // Try the next candidate path.
            }
        }

        return [];
    }

    private static async Task<IReadOnlyList<ServerProfileEntry>> LoadProfileListAsync(
        ServerConfig server,
        string fileName,
        CancellationToken ct)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in ServerPathResolver.GetDataFolderFileCandidates(server.LogPath, fileName)
                     .Concat(ServerPathResolver.GetDataFolderFileCandidates(server.ComposeDataVolumeSource, fileName)))
        {
            if (!seen.Add(path))
                continue;

            ct.ThrowIfCancellationRequested();
            if (!File.Exists(path))
                continue;

            try
            {
                await using var stream = File.OpenRead(path);
                var entries = await JsonSerializer.DeserializeAsync<List<ProfileFileEntry>>(stream, JsonOptions, ct);
                if (entries is null)
                    continue;

                return entries
                    .Where(e => !string.IsNullOrWhiteSpace(e.Uuid))
                    .Select(e => new ServerProfileEntry
                    {
                        Uuid = ServerUserCacheIndex.NormalizeUuid(e.Uuid),
                        Name = e.Name?.Trim() ?? string.Empty
                    })
                    .OrderBy(e => GetSortName(e), StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch
            {
                // Try the next candidate path.
            }
        }

        return [];
    }

    private static string GetSortName(ServerProfileEntry entry) =>
        string.IsNullOrWhiteSpace(entry.Name) ? entry.Uuid : entry.Name;

    private sealed class ProfileFileEntry
    {
        public string Uuid { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }

    private sealed class BanFileEntry
    {
        public string Uuid { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Reason { get; set; }
    }
}
