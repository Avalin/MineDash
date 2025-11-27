# MineDash

A web-based dashboard for managing multiple Minecraft servers via RCON. Built with Blazor Server and .NET 9.0.

<img width="400" height="420" alt="image" src="https://github.com/user-attachments/assets/1e4b0c2e-188c-4676-88cf-88eb07affdf2" />
<img width="600" height="420" alt="minedash-showcase-1" src="https://github.com/user-attachments/assets/a28f0a36-36a2-4867-b7ca-a4917aae7ee2" />

## Features

- 🎮 **Multi-Server Management**: Connect to and manage multiple Minecraft servers from a single interface
- 🧩 **Custom Command Palette:** Add your own frequently used Minecraft commands for quick copy-paste access
- 💻 **Interactive Console**: Execute commands and view real-time responses with command history
- 📊 **Server Logs**: View and monitor server logs in real-time with filtering by log level and thread
- ⏰ **Scheduled Commands**: Automate server commands with flexible scheduling (minutes, hours, weekdays)
- 👥 **User Management**: Multi-user support with admin/non-admin roles
- 🎨 **Customizable Layouts**: Choose from multiple console layout options (1, 2, or 9 consoles)
- 🔒 **Secure RCON**: Connect securely to your Minecraft servers using RCON protocol
- 🐳 **Docker Support**: Easy deployment with Docker and Docker Compose

## Prerequisites

- **For Docker deployment:**
  - Docker and Docker Compose installed
  - Access to your Minecraft server's RCON port

- **For manual deployment:**
  - .NET 9.0 SDK installed
  - Access to your Minecraft server's RCON port

## Quick Start

### Option 1: Docker Compose (Recommended)

1. Clone the repository:
   ```bash
   git clone <repository-url>
   cd MineDash
   ```

2. Edit `compose.yml` to match your setup:
   - Update the volume paths for `app_data` and Minecraft server logs
   - Adjust the network name if needed
   - Modify the port mapping if 8214 conflicts with other services

3. Create the necessary directories:
   ```bash
   mkdir -p /srv/minedash/app_data
   ```

4. Start the container:
   ```bash
   docker compose up -d
   ```

5. Access MineDash at `http://localhost:8214`

### Option 2: Manual Build

1. Clone the repository:
   ```bash
   git clone <repository-url>
   cd MineDash
   ```

2. Restore dependencies:
   ```bash
   dotnet restore
   ```

3. Build the project:
   ```bash
   dotnet build
   ```

4. Run the application:
   ```bash
   dotnet run
   ```

5. Access MineDash at `http://localhost:5248` (or the port shown in the console)

## Configuration

### Adding Minecraft Servers

1. Navigate to the **Manage Servers** page (gear icon in the top right)
2. Click **Add Server**
3. Fill in the server details:
   - **Name**: A friendly name for your server
   - **Host**: The IP address or hostname of your Minecraft server
   - **RCON Port**: The RCON port (usually 25575)
   - **RCON Password**: Your RCON password
   - **Log Path** (optional): Path to your server's `latest.log` file
     - For Docker: Use the host path, e.g., `/srv/minecraft/server-name/logs/latest.log`
   - **Notes** (optional): Any additional notes about the server

4. Click **Save**

### Enabling RCON on Your Minecraft Server

To use MineDash, you need to enable RCON on your Minecraft server:

1. Edit your `server.properties` file:
   ```properties
   enable-rcon=true
   rcon.port=25575
   rcon.password=your-secure-password-here
   ```

2. Restart your Minecraft server

3. Ensure the RCON port is accessible (check firewall rules if needed)

### Data Storage

MineDash stores its data in JSON files:
- **Server configurations**: `app_data/servers.json`
- **Command history**: `app_data/commands.json`
- **Timed commands**: `app_data/timed-commands.json`
- **Users**: `app_data/users.json`
- **Settings**: `app_data/settings.json`

These files are automatically created on first run. Make sure the `app_data` directory is writable by the application.

## Usage

### Adding Consoles

1. Select a server from the **Add console** dropdown
2. Click **Add** to open a new console for that server
3. Multiple consoles can be open simultaneously

### Executing Commands

1. Type your Minecraft command in the console input field
2. Press Enter or click the send button
3. View the response in the console output

### Viewing Logs

If you've configured a log path for your server:
1. Open a console for the server
2. The logs will automatically appear in the log viewer panel
3. Logs update in real-time as they're written to the file
4. Use the filter buttons to filter by log level or thread

### Scheduled Commands

Create automated commands that run on a schedule:
1. Navigate to **Timed Commands** page
2. Click **Add Timed Command**
3. Configure the command, server, and schedule (minutes, hours, weekdays)
4. Commands automatically disable if the server is unavailable (configurable timeout)

### Layout Options

Choose from three layout options:
- **Layout 1**: Single console (full width)
- **Layout 2**: Two consoles side by side
- **Layout 3**: Nine consoles in a grid (3x3)

## Docker Configuration Details

The `compose.yml` file includes:
- Port mapping: `8214:8214` (change if needed)
- Volume mounts:
  - `/srv/minedash/app_data` → Application data persistence
  - `/srv/minecraft` → Read-only access to Minecraft server logs
- Network: Connects to your existing Minecraft network

Adjust these paths to match your server setup.

## Troubleshooting

### Cannot connect to server

- Verify RCON is enabled in `server.properties`
- Check that the RCON port is correct and accessible
- Ensure the RCON password matches exactly
- Check firewall rules if connecting to a remote server

### Logs not showing

- Verify the log path is correct and accessible
- For Docker deployments, ensure the volume mount includes the log directory
- Check file permissions - the application needs read access to the log file

### Port already in use

- Change the port in `compose.yml` (for Docker) or `launchSettings.json` (for manual)
- Update the `ASPNETCORE_URLS` environment variable accordingly

### Data not persisting

- Ensure the `app_data` directory exists and is writable
- For Docker, verify the volume mount is correctly configured
- Check file permissions on the host system

## Development

### Building from Source

```bash
dotnet build -c Release
```

### Running Tests

```bash
dotnet test
```

### Project Structure

- `Components/Pages/` - Blazor page components
- `Services/` - Application services (RCON, storage, logging)
- `Models/` - Data models
- `app_data/` - JSON data storage
- `wwwroot/` - Static web assets
