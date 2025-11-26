using MineDash.Models;

namespace MineDash.Services;

public interface ICommandStore
{
    Task<IReadOnlyList<MinecraftCommand>> GetAllAsync(CancellationToken ct = default);
    Task<MinecraftCommand?> GetByIdAsync(string id, CancellationToken ct = default);
    Task AddOrUpdateAsync(MinecraftCommand command, CancellationToken ct = default);
    Task DeleteAsync(string id, CancellationToken ct = default);
    Task ResetToDefaultsAsync(CancellationToken ct = default);
}

