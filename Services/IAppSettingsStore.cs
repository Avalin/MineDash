using MineDash.Models;

namespace MineDash.Services;

public interface IAppSettingsStore
{
    Task<AppSettings> GetAsync(CancellationToken ct = default);
    Task SaveAsync(AppSettings settings, CancellationToken ct = default);
}

