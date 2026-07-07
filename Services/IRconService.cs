using MineDash.Models;

namespace MineDash.Services;

public interface IRconService
{
    Task<string> SendCommandAsync(
        ServerConfig server,
        string command,
        CancellationToken ct = default);

    Task<ServerOnlineStatus> PingAsync(
        ServerConfig server,
        CancellationToken ct = default);
}
