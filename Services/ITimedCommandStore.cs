using MineDash.Models;

namespace MineDash.Services;

public interface ITimedCommandStore
{
    Task<IReadOnlyList<TimedCommand>> GetAllAsync(CancellationToken ct = default);
    Task<TimedCommand?> GetByIdAsync(string id, CancellationToken ct = default);
    Task AddOrUpdateAsync(TimedCommand command, CancellationToken ct = default);
    Task DeleteAsync(string id, CancellationToken ct = default);
}

