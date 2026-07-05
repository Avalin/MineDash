using CoreRCON;
using MineDash.Models;
using System.Net;
using System.Net.Sockets;

namespace MineDash.Services;

public class RconService : IRconService
{
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

        try
        {
            IPEndPoint endpoint;
            
            // Try to parse as IP address first
            if (IPAddress.TryParse(server.Host, out var parsedAddress))
            {
                endpoint = new IPEndPoint(parsedAddress, server.RconPort);
            }
            else
            {
                // For hostnames (including Docker container names), use DNS resolution
                // Prefer IPv4 addresses as Docker containers typically use IPv4
                IPAddress? ipAddress = null;
                string? resolutionError = null;
                
                // Try GetHostAddressesAsync first (works well for Docker container names)
                try
                {
                    var addresses = await Dns.GetHostAddressesAsync(server.Host, ct);
                    if (addresses.Length > 0)
                    {
                        // Prefer IPv4 addresses for Docker containers
                        ipAddress = addresses.FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork)
                                 ?? addresses.FirstOrDefault();
                    }
                }
                catch (SocketException ex)
                {
                    resolutionError = ex.Message;
                }
                
                // Fallback to GetHostEntryAsync if GetHostAddressesAsync failed
                if (ipAddress == null)
                {
                    try
                    {
                        var hostEntry = await Dns.GetHostEntryAsync(server.Host, ct);
                        if (hostEntry.AddressList.Length > 0)
                        {
                            // Prefer IPv4 addresses for Docker containers
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
                
                if (ipAddress == null)
                {
                    // All resolution methods failed - provide helpful error for Docker containers
                    var errorMsg = $"Could not resolve hostname '{server.Host}'. ";
                    
                    // Check if it looks like a Docker container name (lowercase, hyphens, no dots)
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
                
                endpoint = new IPEndPoint(ipAddress, server.RconPort);
            }

            // Create RCON connection and use it (disposes automatically)
            using var rcon = new RCON(endpoint, server.RconPassword);
            
            // Connect and authenticate with timeout
            try
            {
                await rcon.ConnectAsync();
            }
            catch (Exception ex) when (ex.Message.Contains("Transport endpoint") || ex.Message.Contains("not connected"))
            {
                // If connection fails, try with a fresh endpoint resolution
                // Sometimes the IP address changes or connection state is stale
                throw new InvalidOperationException(
                    $"Failed to establish RCON connection to {server.Name} at {server.Host}:{server.RconPort}. " +
                    $"The connection was established but immediately closed. " +
                    $"Try using the container's IP address directly or ensure both containers are on the same Docker network. " +
                    $"Error: {ex.Message}", ex);
            }
            
            // Send command and get response
            var response = await rcon.SendCommandAsync(command);
            
            return response ?? string.Empty;
        }
        catch (SocketException ex)
        {
            throw new InvalidOperationException(
                $"Failed to connect to {server.Name} at {server.Host}:{server.RconPort}. " +
                $"Check that the server is running and RCON is enabled. " +
                $"Error: {ex.Message}", ex);
        }
        catch (Exception ex) when (ex.Message.Contains("authentication", StringComparison.OrdinalIgnoreCase) ||
                                    ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Authentication failed for {server.Name}. " +
                $"Please check the RCON password in server settings. " +
                $"Error: {ex.Message}", ex);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Error sending command to {server.Name}: {ex.Message}", ex);
        }
    }
}

