using MineDash.Models;

namespace MineDash.Services;

public static class ServerPathResolver
{
    public static string NormalizeServerFolder(string? configuredPath)
    {
        var path = ToServerFolderPath(configuredPath);
        return path.TrimEnd('/', '\\');
    }

    /// <summary>
    /// Files like ops.json and usercache.json live in the server's data folder.
    /// </summary>
    public static IEnumerable<string> GetDataFolderFileCandidates(string? configuredPath, string fileName)
    {
        var folder = NormalizeServerFolder(configuredPath);
        if (string.IsNullOrWhiteSpace(folder))
            yield break;

        if (IsDataFolder(folder))
        {
            yield return CombinePath(folder, fileName);
            yield break;
        }

        yield return CombinePath(folder, $"data/{fileName}");
        yield return CombinePath(folder, fileName);
    }

    public static IEnumerable<string> GetOpsJsonCandidates(ServerConfig server)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in new[] { server.LogPath, server.ComposeDataVolumeSource })
        {
            foreach (var candidate in GetDataFolderFileCandidates(root, "ops.json"))
            {
                if (seen.Add(candidate))
                    yield return candidate;
            }
        }
    }

    public static IEnumerable<string> GetUserCacheCandidates(ServerConfig server)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in new[] { server.LogPath, server.ComposeDataVolumeSource })
        {
            foreach (var candidate in GetDataFolderFileCandidates(root, "usercache.json"))
            {
                if (seen.Add(candidate))
                    yield return candidate;
            }
        }
    }

    private static bool IsDataFolder(string folder) =>
        folder.EndsWith("/data", StringComparison.OrdinalIgnoreCase)
        || folder.EndsWith("\\data", StringComparison.OrdinalIgnoreCase);

    private static string ToServerFolderPath(string? configuredPath)
    {
        var path = configuredPath?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        const string dataLogSuffix = "/data/logs/latest.log";
        if (path.EndsWith(dataLogSuffix, StringComparison.OrdinalIgnoreCase))
            return path[..^dataLogSuffix.Length];

        const string logSuffix = "/logs/latest.log";
        if (path.EndsWith(logSuffix, StringComparison.OrdinalIgnoreCase))
            return path[..^logSuffix.Length];

        const string dataLogsDirSuffix = "/data/logs";
        if (path.EndsWith(dataLogsDirSuffix, StringComparison.OrdinalIgnoreCase))
            return path[..^dataLogsDirSuffix.Length];

        const string logsDirSuffix = "/logs";
        if (path.EndsWith(logsDirSuffix, StringComparison.OrdinalIgnoreCase))
            return path[..^logsDirSuffix.Length];

        return path;
    }

    private static string CombinePath(string basePath, string relativePath) =>
        $"{basePath.TrimEnd('/', '\\')}/{relativePath.TrimStart('/')}";
}
