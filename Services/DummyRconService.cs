using MineDash.Models;

namespace MineDash.Services;

public class DummyRconService : IRconService
{
    public Task<string> SendCommandAsync(ServerConfig server, string command, CancellationToken ct = default)
    {
        var simulatedResponse =
            $"[SIMULATED] Sent '{command}' to {server.Name} ({server.Host}:{server.RconPort})";
        return Task.FromResult(simulatedResponse);
    }
}