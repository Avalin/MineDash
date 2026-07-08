using MineDash.Models;

namespace MineDash.Services;

public interface IDockerComposeImportService
{
    Task<DockerComposeImportResult> ImportFromPathAsync(string composeFilePath, CancellationToken ct = default);
    DockerComposeImportResult ImportFromContent(string composeContent, string? composeFilePath = null);
    ServerConfig CreateServerConfig(DockerComposeServerCandidate candidate, bool managedByCompose, string? existingId = null);
}

public sealed record DockerComposeImportResult(
    IReadOnlyList<DockerComposeServerCandidate> Candidates,
    IReadOnlyList<string> Warnings);

public sealed record DockerComposeServerCandidate
{
    public required string ServiceName { get; init; }
    public string? ComposeFilePath { get; init; }
    public string? ContainerName { get; init; }
    public string? Image { get; init; }
    public string? Version { get; init; }
    public string? Host { get; init; }
    public int RconPort { get; init; } = 25575;
    public string? RconPassword { get; init; }
    public string? DataVolumeSource { get; init; }
    public string? DataVolumeTarget { get; init; }
    public string? Memory { get; init; }
    public bool LooksLikeMinecraftServer { get; init; }
}
