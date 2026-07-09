using CoreRCON;
using MineDash.Models;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace MineDash.Services;

public sealed class RconService : IRconService, IAsyncDisposable
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(2);

    private readonly ConcurrentDictionary<string, ManagedConnection> _connections = new(StringComparer.Ordinal);

    public Task<ServerOnlineStatus> CheckReachabilityAsync(
        ServerConfig server,
        CancellationToken ct = default)
    {
        if (server is null)
            throw new ArgumentNullException(nameof(server));

        if (string.IsNullOrWhiteSpace(server.Host))
            return Task.FromResult(ServerOnlineStatus.Offline);

        var entry = GetOrCreateEntry(server);
        return Task.FromResult(ReadCachedStatus(entry));
    }

    public Task<ServerOnlineStatus> PingAsync(
        ServerConfig server,
        CancellationToken ct = default) =>
        EnsureConnectedAsync(server, ct);

    public async Task<string> SendCommandAsync(
        ServerConfig server,
        string command,
        CancellationToken ct = default)
    {
        if (server == null)
            throw new ArgumentNullException(nameof(server));

        if (string.IsNullOrWhiteSpace(command))
            throw new ArgumentException("Command cannot be null or empty", nameof(command));

        if (string.IsNullOrWhiteSpace(server.Host))
            throw new ArgumentException("Server host cannot be null or empty", nameof(server));

        command = command.Trim();
        if (command.StartsWith('/'))
            command = command[1..];

        var entry = GetOrCreateEntry(server);
        await entry.Gate.WaitAsync(ct);
        try
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    var status = await ConnectLockedAsync(entry, server, ct);
                    if (status != ServerOnlineStatus.Online)
                        throw CreateStatusException(server, status);

                    var response = await entry.Client!.SendCommandAsync(command);
                    entry.LastStatus = ServerOnlineStatus.Online;
                    return response ?? string.Empty;
                }
                catch (Exception ex) when (attempt == 0 && ShouldRetryAfterFailure(ex))
                {
                    await DisconnectLockedAsync(entry);
                }
            }

            throw new InvalidOperationException($"Error sending command to {server.Name}.");
        }
        catch (SocketException ex)
        {
            throw new InvalidOperationException(
                $"Failed to connect to {server.Name} at {server.Host}:{server.RconPort}. " +
                $"Check that the server is running and RCON is enabled. " +
                $"Error: {ex.Message}", ex);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex) when (IsAuthFailure(ex))
        {
            entry.LastStatus = ServerOnlineStatus.AuthError;
            throw new InvalidOperationException(
                $"Authentication failed for {server.Name}. " +
                $"Please check the RCON password in server settings. " +
                $"Error: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            entry.LastStatus = ServerOnlineStatus.Offline;
            throw new InvalidOperationException(
                $"Error sending command to {server.Name}: {ex.Message}", ex);
        }
        finally
        {
            entry.Gate.Release();
        }
    }

    public async Task<ServerOnlineStatus> EnsureConnectedAsync(
        ServerConfig server,
        CancellationToken ct = default)
    {
        if (server is null)
            throw new ArgumentNullException(nameof(server));

        if (string.IsNullOrWhiteSpace(server.Host))
            return ServerOnlineStatus.Offline;

        var entry = GetOrCreateEntry(server);
        await entry.Gate.WaitAsync(ct);
        try
        {
            return await ConnectLockedAsync(entry, server, ct);
        }
        finally
        {
            entry.Gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var entry in _connections.Values)
            await entry.DisposeAsync();

        _connections.Clear();
    }

    private ManagedConnection GetOrCreateEntry(ServerConfig server)
    {
        var fingerprint = BuildFingerprint(server);
        var entry = _connections.GetOrAdd(server.Id, _ => new ManagedConnection(server.Id));

        if (!string.Equals(entry.Fingerprint, fingerprint, StringComparison.Ordinal))
        {
            entry.Gate.Wait();
            try
            {
                if (!string.Equals(entry.Fingerprint, fingerprint, StringComparison.Ordinal))
                {
                    entry.DisconnectSync();
                    entry.Fingerprint = fingerprint;
                    entry.LastStatus = ServerOnlineStatus.Unknown;
                }
            }
            finally
            {
                entry.Gate.Release();
            }
        }

        return entry;
    }

    private static ServerOnlineStatus ReadCachedStatus(ManagedConnection entry)
    {
        if (entry.Client is { Connected: true, Authenticated: true })
            return ServerOnlineStatus.Online;

        return entry.LastStatus;
    }

    private async Task<ServerOnlineStatus> ConnectLockedAsync(
        ManagedConnection entry,
        ServerConfig server,
        CancellationToken ct)
    {
        if (entry.Client is { Connected: true, Authenticated: true })
        {
            entry.LastStatus = ServerOnlineStatus.Online;
            return ServerOnlineStatus.Online;
        }

        await DisconnectLockedAsync(entry);

        try
        {
            var endpoint = await ResolveEndpointAsync(server, ct);
            var client = new RCON(endpoint, server.RconPassword);
            client.OnDisconnected += entry.HandleDisconnected;

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(ConnectTimeout);

            var connectTask = client.ConnectAsync();
            if (await Task.WhenAny(connectTask, Task.Delay(ConnectTimeout, timeoutCts.Token)) != connectTask)
            {
                client.OnDisconnected -= entry.HandleDisconnected;
                client.Dispose();
                entry.LastStatus = ServerOnlineStatus.Offline;
                return ServerOnlineStatus.Offline;
            }

            await connectTask;
            entry.Client = client;
            entry.LastStatus = ServerOnlineStatus.Online;
            return ServerOnlineStatus.Online;
        }
        catch (Exception ex) when (IsAuthFailure(ex))
        {
            entry.LastStatus = ServerOnlineStatus.AuthError;
            return ServerOnlineStatus.AuthError;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex.Message.Contains("Transport endpoint", StringComparison.OrdinalIgnoreCase)
                                   || ex.Message.Contains("not connected", StringComparison.OrdinalIgnoreCase))
        {
            entry.LastStatus = ServerOnlineStatus.Offline;
            throw new InvalidOperationException(
                $"Failed to establish RCON connection to {server.Name} at {server.Host}:{server.RconPort}. " +
                $"The connection was established but immediately closed. " +
                $"Try using the container's IP address directly or ensure both containers are on the same Docker network. " +
                $"Error: {ex.Message}", ex);
        }
        catch
        {
            entry.LastStatus = ServerOnlineStatus.Offline;
            return ServerOnlineStatus.Offline;
        }
    }

    private static async Task DisconnectLockedAsync(ManagedConnection entry)
    {
        if (entry.Client is null)
            return;

        var client = entry.Client;
        entry.Client = null;
        client.OnDisconnected -= entry.HandleDisconnected;

        try
        {
            client.Dispose();
        }
        catch
        {
        }
    }

    private static InvalidOperationException CreateStatusException(ServerConfig server, ServerOnlineStatus status) =>
        status switch
        {
            ServerOnlineStatus.AuthError => new InvalidOperationException(
                $"Authentication failed for {server.Name}. Please check the RCON password in server settings."),
            _ => new InvalidOperationException(
                $"Failed to connect to {server.Name} at {server.Host}:{server.RconPort}. " +
                $"Check that the server is running and RCON is enabled.")
        };

    private static bool ShouldRetryAfterFailure(Exception ex) =>
        ex is SocketException
        || ex.Message.Contains("not connected", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("Transport endpoint", StringComparison.OrdinalIgnoreCase);

    private static string BuildFingerprint(ServerConfig server) =>
        $"{server.Host}\0{server.RconPort}\0{server.RconPassword}";

    private static async Task<IPEndPoint> ResolveEndpointAsync(ServerConfig server, CancellationToken ct)
    {
        if (IPAddress.TryParse(server.Host, out var parsedAddress))
            return new IPEndPoint(parsedAddress, server.RconPort);

        IPAddress? ipAddress = null;
        string? resolutionError = null;

        try
        {
            var addresses = await Dns.GetHostAddressesAsync(server.Host, ct);
            if (addresses.Length > 0)
            {
                ipAddress = addresses.FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork)
                         ?? addresses.FirstOrDefault();
            }
        }
        catch (SocketException ex)
        {
            resolutionError = ex.Message;
        }

        if (ipAddress is null)
        {
            try
            {
                var hostEntry = await Dns.GetHostEntryAsync(server.Host, ct);
                if (hostEntry.AddressList.Length > 0)
                {
                    ipAddress = hostEntry.AddressList.FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork)
                             ?? hostEntry.AddressList[0];
                }
            }
            catch (SocketException ex)
            {
                if (string.IsNullOrEmpty(resolutionError))
                    resolutionError = ex.Message;
            }
        }

        if (ipAddress is null)
        {
            var errorMsg = $"Could not resolve hostname '{server.Host}'. ";

            if (server.Host.Contains('-') && !server.Host.Contains('.'))
            {
                errorMsg += $"\n\nThis looks like a Docker container name. Docker container names only resolve within Docker networks.\n\n" +
                           $"Solutions:\n" +
                           $"1. If RCON port is mapped to host: Use 'localhost' as the Host instead\n" +
                           $"   (e.g., docker run -p 25575:25575 ...)\n\n" +
                           $"2. Find container IP and use that:\n" +
                           $"   docker inspect {server.Host} | findstr IPAddress\n\n" +
                           $"3. Run MineDash in the same Docker network as your Minecraft server\n\n" +
                           $"4. Use Docker Compose and ensure both containers are on the same network";
            }
            else
            {
                errorMsg += $"\n\nError details: {resolutionError ?? "Unknown error"}";
            }

            throw new InvalidOperationException(errorMsg);
        }

        return new IPEndPoint(ipAddress, server.RconPort);
    }

    private static bool IsAuthFailure(Exception ex) =>
        ex.Message.Contains("authentication", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase);

    private sealed class ManagedConnection : IAsyncDisposable
    {
        public ManagedConnection(string serverId) => ServerId = serverId;

        public string ServerId { get; }
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public RCON? Client { get; set; }
        public string Fingerprint { get; set; } = string.Empty;
        public ServerOnlineStatus LastStatus { get; set; } = ServerOnlineStatus.Unknown;

        public void HandleDisconnected()
        {
            Client = null;
            LastStatus = ServerOnlineStatus.Offline;
        }

        public void DisconnectSync()
        {
            if (Client is null)
                return;

            var client = Client;
            Client = null;
            client.OnDisconnected -= HandleDisconnected;

            try
            {
                client.Dispose();
            }
            catch
            {
            }
        }

        public async ValueTask DisposeAsync()
        {
            await Gate.WaitAsync();
            try
            {
                if (Client is null)
                    return;

                var client = Client;
                Client = null;
                client.OnDisconnected -= HandleDisconnected;

                try
                {
                    client.Dispose();
                }
                catch
                {
                }
            }
            finally
            {
                Gate.Release();
                Gate.Dispose();
            }
        }
    }
}
