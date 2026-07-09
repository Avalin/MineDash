using MineDash.Models;

namespace MineDash.Services;

public interface IRconService
{
    Task<string> SendCommandAsync(
        ServerConfig server,
        string command,
        CancellationToken ct = default);

    /// <summary>Returns cached status for the server's persistent RCON connection without opening a new one.</summary>
    Task<ServerOnlineStatus> CheckReachabilityAsync(
        ServerConfig server,
        CancellationToken ct = default);

    /// <summary>Connects or reuses the persistent RCON session for this server.</summary>
    Task<ServerOnlineStatus> PingAsync(
        ServerConfig server,
        CancellationToken ct = default);

    /// <summary>Opens the persistent RCON session when it is missing or disconnected.</summary>
    Task<ServerOnlineStatus> EnsureConnectedAsync(
        ServerConfig server,
        CancellationToken ct = default);
}
