using MineDash.Models;
using YamlDotNet.RepresentationModel;

namespace MineDash.Services;

public sealed class DockerComposeImportService : IDockerComposeImportService
{
    private static readonly StringComparer KeyComparer = StringComparer.OrdinalIgnoreCase;

    public async Task<DockerComposeImportResult> ImportFromPathAsync(
        string composeFilePath,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(composeFilePath))
            return new DockerComposeImportResult([], ["Compose file path is required."]);

        var trimmedPath = composeFilePath.Trim();
        if (!File.Exists(trimmedPath))
            return new DockerComposeImportResult([], [$"Compose file not found: {trimmedPath}"]);

        var content = await File.ReadAllTextAsync(trimmedPath, ct);
        return ImportFromContent(content, trimmedPath);
    }

    public DockerComposeImportResult ImportFromContent(string composeContent, string? composeFilePath = null)
    {
        var warnings = new List<string>();
        if (string.IsNullOrWhiteSpace(composeContent))
            return new DockerComposeImportResult([], ["Compose file is empty."]);

        var yaml = new YamlStream();
        try
        {
            yaml.Load(new StringReader(composeContent));
        }
        catch (Exception ex)
        {
            return new DockerComposeImportResult([], [$"Could not parse compose YAML: {ex.Message}"]);
        }

        if (yaml.Documents.Count == 0 || yaml.Documents[0].RootNode is not YamlMappingNode root)
            return new DockerComposeImportResult([], ["Compose file does not contain a YAML mapping."]);

        if (!TryGetMapping(root, "services", out var services))
            return new DockerComposeImportResult([], ["Compose file does not contain a services section."]);

        var candidates = new List<DockerComposeServerCandidate>();
        foreach (var serviceEntry in services.Children)
        {
            if (serviceEntry.Key is not YamlScalarNode serviceKey ||
                serviceEntry.Value is not YamlMappingNode service)
            {
                continue;
            }

            var serviceName = serviceKey.Value?.Trim();
            if (string.IsNullOrWhiteSpace(serviceName))
                continue;

            var env = ReadEnvironment(service);
            var ports = ReadPorts(service);
            var volumes = ReadVolumes(service, composeFilePath);
            var image = GetScalar(service, "image");
            var containerName = GetScalar(service, "container_name");
            var dataVolume = ChooseDataVolume(volumes);
            var rconPort = GetRconPort(env, ports);
            var version = GetFirstValue(env, "VERSION", "MINECRAFT_VERSION")
                          ?? GetImageTag(image);
            var memory = GetFirstValue(env, "MEMORY", "MAX_MEMORY", "JVM_MEMORY")
                         ?? GetScalar(service, "mem_limit")
                         ?? GetDeployMemoryLimit(service);
            var looksLikeMinecraft = LooksLikeMinecraftService(
                serviceName,
                containerName,
                image,
                env,
                ports,
                dataVolume);

            if (!looksLikeMinecraft)
                continue;

            candidates.Add(new DockerComposeServerCandidate
            {
                ServiceName = serviceName,
                ComposeFilePath = composeFilePath,
                ContainerName = containerName,
                Image = image,
                Version = version,
                Host = FirstNonEmpty(containerName, serviceName),
                RconPort = rconPort,
                RconPassword = GetFirstValue(
                    env,
                    "RCON_PASSWORD",
                    "MINECRAFT_RCON_PASSWORD",
                    "RCON_PASSWORD_FILE"),
                DataVolumeSource = dataVolume?.Source,
                DataVolumeTarget = dataVolume?.Target,
                Memory = memory,
                LooksLikeMinecraftServer = true
            });
        }

        if (candidates.Count == 0)
            warnings.Add("No Minecraft-like services were found in the compose file.");

        return new DockerComposeImportResult(
            candidates.OrderBy(c => c.ServiceName, StringComparer.OrdinalIgnoreCase).ToList(),
            warnings);
    }

    public ServerConfig CreateServerConfig(
        DockerComposeServerCandidate candidate,
        bool managedByCompose,
        string? existingId = null)
    {
        var name = FirstNonEmpty(candidate.ContainerName, candidate.ServiceName) ?? "Minecraft Server";

        return new ServerConfig
        {
            Id = string.IsNullOrWhiteSpace(existingId)
                ? Guid.NewGuid().ToString("N")
                : existingId,
            Name = ToDisplayName(name),
            ConfigSource = managedByCompose
                ? ServerConfigSource.DockerCompose
                : ServerConfigSource.Manual,
            Host = FirstNonEmpty(candidate.Host, candidate.ContainerName, candidate.ServiceName) ?? "localhost",
            RconPort = candidate.RconPort <= 0 ? 25575 : candidate.RconPort,
            RconPassword = candidate.RconPassword ?? string.Empty,
            LogPath = NormalizePath(candidate.DataVolumeSource),
            LogTimeZoneId = "UTC",
            ComposeFilePath = managedByCompose ? candidate.ComposeFilePath : null,
            ComposeServiceName = managedByCompose ? candidate.ServiceName : null,
            ComposeContainerName = managedByCompose ? candidate.ContainerName : null,
            ComposeImage = managedByCompose ? candidate.Image : null,
            ComposeVersion = managedByCompose ? candidate.Version : null,
            ComposeDataVolumeSource = managedByCompose ? candidate.DataVolumeSource : null,
            ComposeDataVolumeTarget = managedByCompose ? candidate.DataVolumeTarget : null,
            ComposeMemory = managedByCompose ? candidate.Memory : null
        };
    }

    private static Dictionary<string, string> ReadEnvironment(YamlMappingNode service)
    {
        var values = new Dictionary<string, string>(KeyComparer);
        if (!TryGetNode(service, "environment", out var environment))
            return values;

        switch (environment)
        {
            case YamlMappingNode mapping:
                foreach (var entry in mapping.Children)
                {
                    var key = ScalarValue(entry.Key);
                    if (!string.IsNullOrWhiteSpace(key))
                        values[key] = ScalarValue(entry.Value) ?? string.Empty;
                }
                break;
            case YamlSequenceNode sequence:
                foreach (var item in sequence.Children)
                {
                    var pair = ScalarValue(item);
                    if (string.IsNullOrWhiteSpace(pair))
                        continue;

                    var separatorIndex = pair.IndexOf('=');
                    if (separatorIndex <= 0)
                        continue;

                    values[pair[..separatorIndex].Trim()] = pair[(separatorIndex + 1)..].Trim();
                }
                break;
        }

        return values;
    }

    private static List<ComposePort> ReadPorts(YamlMappingNode service)
    {
        var values = new List<ComposePort>();
        if (!TryGetNode(service, "ports", out var ports) || ports is not YamlSequenceNode sequence)
            return values;

        foreach (var item in sequence.Children)
        {
            switch (item)
            {
                case YamlScalarNode scalar:
                    var parsed = ParsePortString(scalar.Value);
                    if (parsed is not null)
                        values.Add(parsed);
                    break;
                case YamlMappingNode mapping:
                    var target = ParseInt(GetScalar(mapping, "target"));
                    var published = ParseInt(GetScalar(mapping, "published"));
                    if (target is not null || published is not null)
                    {
                        values.Add(new ComposePort(
                            published ?? target ?? 0,
                            target ?? published ?? 0));
                    }
                    break;
            }
        }

        return values;
    }

    private static List<ComposeVolume> ReadVolumes(YamlMappingNode service, string? composeFilePath)
    {
        var values = new List<ComposeVolume>();
        if (!TryGetNode(service, "volumes", out var volumes) || volumes is not YamlSequenceNode sequence)
            return values;

        var composeDirectory = string.IsNullOrWhiteSpace(composeFilePath)
            ? null
            : Path.GetDirectoryName(composeFilePath);

        foreach (var item in sequence.Children)
        {
            switch (item)
            {
                case YamlScalarNode scalar:
                    var parsed = ParseVolumeString(scalar.Value, composeDirectory);
                    if (parsed is not null)
                        values.Add(parsed);
                    break;
                case YamlMappingNode mapping:
                    var source = GetScalar(mapping, "source");
                    var target = GetScalar(mapping, "target");
                    if (!string.IsNullOrWhiteSpace(source) && !string.IsNullOrWhiteSpace(target))
                    {
                        values.Add(new ComposeVolume(
                            ResolveVolumeSource(source, composeDirectory),
                            target));
                    }
                    break;
            }
        }

        return values;
    }

    private static ComposeVolume? ChooseDataVolume(List<ComposeVolume> volumes)
    {
        return volumes
            .OrderByDescending(v => string.Equals(v.Target, "/data", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(v => v.Target.Contains("data", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(v => v.Target.Contains("server", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(v => v.Target.Contains("minecraft", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault(v =>
                v.Target.Contains("data", StringComparison.OrdinalIgnoreCase) ||
                v.Target.Contains("server", StringComparison.OrdinalIgnoreCase) ||
                v.Target.Contains("minecraft", StringComparison.OrdinalIgnoreCase));
    }

    private static bool LooksLikeMinecraftService(
        string serviceName,
        string? containerName,
        string? image,
        Dictionary<string, string> env,
        List<ComposePort> ports,
        ComposeVolume? dataVolume)
    {
        if (ContainsMinecraftHint(serviceName) ||
            ContainsMinecraftHint(containerName) ||
            ContainsMinecraftHint(image))
        {
            return true;
        }

        if (env.ContainsKey("EULA") ||
            env.ContainsKey("RCON_PASSWORD") ||
            env.ContainsKey("ENABLE_RCON") ||
            env.ContainsKey("MINECRAFT_VERSION") ||
            env.ContainsKey("VERSION"))
        {
            return true;
        }

        return ports.Any(p => p.Target == 25565 || p.Target == 25575) ||
               string.Equals(dataVolume?.Target, "/data", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsMinecraftHint(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        (value.Contains("minecraft", StringComparison.OrdinalIgnoreCase) ||
         value.Contains("itzg", StringComparison.OrdinalIgnoreCase) ||
         value.Contains("paper", StringComparison.OrdinalIgnoreCase) ||
         value.Contains("fabric", StringComparison.OrdinalIgnoreCase) ||
         value.Contains("forge", StringComparison.OrdinalIgnoreCase));

    private static int GetRconPort(Dictionary<string, string> env, List<ComposePort> ports)
    {
        var envPort = ParseInt(GetFirstValue(env, "RCON_PORT", "MINECRAFT_RCON_PORT"));
        if (envPort is not null)
            return envPort.Value;

        var rconPort = ports.FirstOrDefault(p => p.Target == 25575);
        if (rconPort is not null && rconPort.Target > 0)
            return rconPort.Target;

        return 25575;
    }

    private static string? GetDeployMemoryLimit(YamlMappingNode service)
    {
        if (!TryGetMapping(service, "deploy", out var deploy) ||
            !TryGetMapping(deploy, "resources", out var resources) ||
            !TryGetMapping(resources, "limits", out var limits))
        {
            return null;
        }

        return GetScalar(limits, "memory");
    }

    private static ComposePort? ParsePortString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var withoutProtocol = value.Split('/', 2)[0];
        var parts = withoutProtocol.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return null;

        var target = ParseInt(parts[^1]);
        var published = parts.Length >= 2 ? ParseInt(parts[^2]) : target;
        if (target is null && published is null)
            return null;

        return new ComposePort(published ?? target ?? 0, target ?? published ?? 0);
    }

    private static ComposeVolume? ParseVolumeString(string? value, string? composeDirectory)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var parts = value.Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
            return null;

        return new ComposeVolume(
            ResolveVolumeSource(parts[0], composeDirectory),
            parts[1]);
    }

    private static string ResolveVolumeSource(string source, string? composeDirectory)
    {
        if (string.IsNullOrWhiteSpace(source))
            return source;

        var trimmed = source.Trim();
        if (Path.IsPathRooted(trimmed) || string.IsNullOrWhiteSpace(composeDirectory))
            return NormalizePath(trimmed) ?? trimmed;

        if (trimmed.StartsWith(".", StringComparison.Ordinal))
            return NormalizePath(Path.GetFullPath(Path.Combine(composeDirectory, trimmed))) ?? trimmed;

        return trimmed;
    }

    private static bool TryGetMapping(
        YamlMappingNode mapping,
        string key,
        out YamlMappingNode value)
    {
        value = null!;
        if (!TryGetNode(mapping, key, out var node) || node is not YamlMappingNode childMapping)
            return false;

        value = childMapping;
        return true;
    }

    private static bool TryGetNode(YamlMappingNode mapping, string key, out YamlNode node)
    {
        foreach (var entry in mapping.Children)
        {
            if (entry.Key is YamlScalarNode scalar &&
                string.Equals(scalar.Value, key, StringComparison.OrdinalIgnoreCase))
            {
                node = entry.Value;
                return true;
            }
        }

        node = null!;
        return false;
    }

    private static string? GetScalar(YamlMappingNode mapping, string key) =>
        TryGetNode(mapping, key, out var node) ? ScalarValue(node) : null;

    private static string? ScalarValue(YamlNode node) =>
        node switch
        {
            YamlScalarNode scalar => scalar.Value?.Trim(),
            _ => null
        };

    private static string? GetFirstValue(Dictionary<string, string> values, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (values.TryGetValue(key, out var value) &&
                !string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static string? GetImageTag(string? image)
    {
        if (string.IsNullOrWhiteSpace(image))
            return null;

        var slashIndex = image.LastIndexOf('/');
        var tagIndex = image.LastIndexOf(':');
        if (tagIndex <= slashIndex)
            return null;

        return image[(tagIndex + 1)..];
    }

    private static int? ParseInt(string? value) =>
        int.TryParse(value, out var parsed) ? parsed : null;

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private static string ToDisplayName(string value) =>
        string.Join(
            " ",
            value.Replace('-', ' ')
                .Replace('_', ' ')
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));

    private static string? NormalizePath(string? path) =>
        string.IsNullOrWhiteSpace(path)
            ? null
            : path.Replace('\\', '/').TrimEnd('/');

    private sealed record ComposePort(int Published, int Target);
    private sealed record ComposeVolume(string Source, string Target);
}
