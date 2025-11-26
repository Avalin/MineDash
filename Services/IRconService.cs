using MineDash.Models;

namespace MineDash.Services;

public interface IRconService
{
    Task<string> SendCommandAsync(
        ServerConfig server, 
        string command, CancellationToken 
        ct = default
    );
}