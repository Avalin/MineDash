namespace MineDash.Services;

public interface IPlayerAvatarService
{
    Task<string?> GetAvatarPathAsync(string playerName, CancellationToken ct = default);
}

public sealed class PlayerAvatarService : IPlayerAvatarService
{
    private const string AvatarSourceBaseUrl = "https://mc-heads.net/avatar/";
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(8);

    private readonly HttpClient _httpClient;
    private readonly string _cacheDirectory;

    public PlayerAvatarService(HttpClient httpClient, IWebHostEnvironment env)
    {
        _httpClient = httpClient;
        _httpClient.Timeout = RequestTimeout;

        _cacheDirectory = Path.Combine(env.ContentRootPath, "app_data", "skin-cache");
        Directory.CreateDirectory(_cacheDirectory);
    }

    public async Task<string?> GetAvatarPathAsync(string playerName, CancellationToken ct = default)
    {
        if (!IsValidPlayerName(playerName))
            return null;

        var cachePath = GetCachePath(playerName);
        if (File.Exists(cachePath))
            return cachePath;

        var url = $"{AvatarSourceBaseUrl}{Uri.EscapeDataString(playerName)}/32";
        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
            return null;

        var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
        if (!contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            return null;

        await using var networkStream = await response.Content.ReadAsStreamAsync(ct);
        var tempPath = cachePath + ".tmp";

        try
        {
            await using (var fileStream = File.Create(tempPath))
                await networkStream.CopyToAsync(fileStream, ct);

            File.Move(tempPath, cachePath, overwrite: true);
            return cachePath;
        }
        catch
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);

            throw;
        }
    }

    private string GetCachePath(string playerName) =>
        Path.Combine(_cacheDirectory, $"{playerName.ToLowerInvariant()}.png");

    private static bool IsValidPlayerName(string name) =>
        name.Length is >= 3 and <= 16
        && name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');
}
