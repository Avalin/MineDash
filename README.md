# MineDash

A web-based dashboard for managing multiple Minecraft servers via RCON. Built with Blazor Server and .NET 10.

<img width="400" height="420" alt="image" src="https://github.com/user-attachments/assets/1e4b0c2e-188c-4676-88cf-88eb07affdf2" />
<img width="600" height="420" alt="minedash-showcase-1" src="https://github.com/user-attachments/assets/a28f0a36-36a2-4867-b7ca-a4917aae7ee2" />

## Features

- 🎮 **Multi-Server Management**: Connect to and manage multiple Minecraft servers from a single interface
- 🧩 **Per-User Command Palette**: Add frequently used Minecraft commands for quick access
- 💻 **Interactive Console**: Execute commands and view real-time responses with persisted command activity
- 📊 **Server Logs**: Monitor server logs in real time with history, level, and thread filtering
- ⏰ **Scheduled Commands**: Automate server commands with flexible scheduling (minutes, hours, weekdays)
- 👥 **User Management**: Multi-user support with admin/non-admin roles
- 🎨 **Customizable Layouts**: Arrange open consoles in 1, 2, or 3-column layouts
- 🔐 **RCON Integration**: Connect to Minecraft servers using password-protected RCON
- 🐳 **Docker Support**: Easy deployment plus Docker Compose import for managed Minecraft servers

## Prerequisites

- **For Docker deployment:**
  - Docker and Docker Compose installed
  - Access to your Minecraft server's RCON port

- **For manual deployment:**
  - .NET 10 SDK installed
  - Access to your Minecraft server's RCON port

## Quick Start

### Option 1: Docker Compose (Recommended)

1. Clone the repository:
   ```bash
   git clone https://github.com/Avalin/MineDash.git
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
6. Create the first user from the login page. The first account automatically becomes an administrator.

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
6. Create the first user from the login page. The first account automatically becomes an administrator.

## Configuration

### Adding Minecraft Servers

MineDash supports two server setup modes:

- **Import Docker Compose**: Recommended for Docker-managed Minecraft servers. MineDash parses a Minecraft server `docker-compose.yml`, extracts RCON and volume settings, and saves the compose file path so the server can be reloaded later. Imported values are read-only because the compose file is the source of truth.
- **Add Server**: Manual setup for vanilla servers on Windows, Paper servers on Linux, hosted servers, non-Docker servers, or any server where you want to enter all details yourself.

### Importing Docker Compose Servers

1. Navigate to the **Manage Servers** page (gear icon in the top right)
2. Click **Import Docker Compose**
3. Choose one of the import modes:
   - **Compose-managed server**: Enter a path to a `docker-compose.yml` that is readable by the MineDash server/container, for example `/srv/minecraft/creatamon/docker-compose.yml`
   - **One-time import**: Upload a compose file from your browser to create a normal editable server
4. If the compose file contains multiple Minecraft-like services, choose the service to import
5. For compose-managed servers, use **Reload from compose** after changing the compose file

MineDash extracts values such as the service/container name, RCON host, RCON port, RCON password, data volume, image/version, and memory settings when they are present in the compose file.

For path-based compose imports, the compose file path must be visible inside the MineDash runtime. In Docker, mount the compose file or its parent folder read-only if needed.

### Adding Manual Servers

1. Navigate to the **Manage Servers** page (gear icon in the top right)
2. Click **Add Server**
3. Fill in the server details:
   - **Name**: A friendly name for your server
   - **Host**: The IP address or hostname of your Minecraft server
   - **RCON Port**: The RCON port (usually 25575)
   - **RCON Password**: Your RCON password
   - **Server Folder Path** (optional): Path to the Minecraft server folder **inside the MineDash container**
     - Example: `/srv/minecraft/creatamon`
     - MineDash looks for `data/logs/latest.log` and `logs/latest.log` under this folder
     - Older direct `latest.log` paths are still supported
     - This is not your Windows `\\NAS\minecraft\...` path — map the NAS folder into Docker first
     - Use **Test server access** on the server edit form to verify the folder, log file, and `server.properties`
   - **Log Timezone** (optional): Timezone used by timestamps in `latest.log` (Docker servers commonly log in UTC)

4. Click **Save**

### Time Display

MineDash can display log and command timestamps in a chosen timezone. For Docker, set:

```yaml
environment:
  - MineDash__DisplayTimeZoneId=Europe/Oslo
```

If this value is not set, MineDash uses the host's local timezone. Server-specific **Log Timezone** controls how timestamps from each Minecraft `latest.log` file are interpreted before display.

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
- **Command palette**: `app_data/users/<username>/commands.json`
- **Console activity**: `app_data/console_activity.json`
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
- **Layout 2**: Up to two console columns
- **Layout 3**: Up to three console columns

## Docker Configuration Details

The `compose.yml` file includes:
- Port mapping: `8214:8214` (change if needed)
- Volume mounts:
  - `/srv/minedash/app_data` → Application data persistence
  - `/srv/minecraft` → Read-only access to Minecraft server logs
  - Mount Minecraft server compose files or parent folders read-only if you want path-based Compose import/reload
- Environment:
  - `ASPNETCORE_URLS=http://+:8214`
  - `MineDash__DisplayTimeZoneId=Europe/Oslo` (change to your preferred display timezone)
- Network: Connects to your existing Minecraft network

Adjust these paths to match your server setup.

## Troubleshooting

### Cannot connect to server

- Verify RCON is enabled in `server.properties`
- Check that the RCON port is correct and accessible
- Ensure the RCON password matches exactly
- Check firewall rules if connecting to a remote server

### Logs not showing

- The log path must be readable **inside the MineDash container**, not just on your PC/NAS file share
- On **Synology**, mount your minecraft share in Docker/Compose, e.g. `/volume1/minecraft:/srv/minecraft:ro`
- Then configure the in-container server folder, e.g. `/srv/minecraft/creatamon`
- MineDash will try both `/srv/minecraft/creatamon/data/logs/latest.log` and `/srv/minecraft/creatamon/logs/latest.log`
- Use **Server Management → Test server access** to see mount diagnostics
- Check file permissions — MineDash needs read access to `latest.log`

### Synology / NAS setup

If FileBrowser shows `minecraft/creatamon/data/logs`, that maps to something like `/volume1/minecraft/creatamon/data/logs` on the NAS host. MineDash cannot read SMB paths directly. In `compose.yml` (or Container Manager):

```yaml
volumes:
  - /volume1/minecraft:/srv/minecraft:ro
```

Server log path in MineDash:

```text
/srv/minecraft/creatamon
```

### Port already in use

- Change the port in `compose.yml` (for Docker) or `launchSettings.json` (for manual)
- Update the `ASPNETCORE_URLS` environment variable accordingly

### Data not persisting

- Ensure the `app_data` directory exists and is writable
- For Docker, verify the volume mount is correctly configured
- Check file permissions on the host system

## Development

### Building the Docker image

On Windows (checks that Docker Desktop is running first):

```powershell
.\scripts\docker-build.ps1
.\scripts\docker-build.ps1 -Push
```

On Linux/macOS:

```bash
./scripts/docker-build.sh
PUSH=1 ./scripts/docker-build.sh
```

If Docker isn't running, these scripts show a clear message instead of the raw pipe error.

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
