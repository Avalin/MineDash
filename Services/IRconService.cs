using MineDash.Models;

namespace MineDash.Services;

public interface IRconService
{
    Task<string> SendCommandAsync(
        ServerConfig server,
        string command,
        CancellationToken ct = default);

    /// <summary>Opens the RCON port without completing an RCON handshake (no server log spam).</summary>
    Task<ServerOnlineStatus> CheckReachabilityAsync(
        ServerConfig server,
        CancellationToken ct = default);

    /// <summary>Full RCON connect + auth. Use sparingly — each call creates a server-side RCON client thread.</summary>
    Task<ServerOnlineStatus> PingAsync(
        ServerConfig server,
        CancellationToken ct = default);
}
