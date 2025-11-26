using MineDash.Models;

namespace MineDash.Services;

public interface IServerConfigStore
{
    Task<IReadOnlyList<ServerConfig>> GetAllAsync(CancellationToken ct = default);
    Task<ServerConfig?> GetByIdAsync(string id, CancellationToken ct = default);
    Task AddOrUpdateAsync(ServerConfig server, CancellationToken ct = default);
    Task DeleteAsync(string id, CancellationToken ct = default);
}