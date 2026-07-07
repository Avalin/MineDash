using System.Text.Json;
using MineDash.Models;
using Microsoft.JSInterop;

namespace MineDash.Services;

public interface IConsoleSessionPersistence
{
    Task<HomeConsolePersistedState?> LoadAsync();
    Task SaveAsync(HomeConsolePersistedState state);
}

public sealed class BrowserConsoleSessionPersistence : IConsoleSessionPersistence
{
    private const string StorageKey = "minedash_console_state";

    private readonly IJSRuntime _jsRuntime;

    public BrowserConsoleSessionPersistence(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    public async Task<HomeConsolePersistedState?> LoadAsync()
    {
        try
        {
            var stateJson = await _jsRuntime.InvokeAsync<string>("localStorage.getItem", StorageKey);
            if (string.IsNullOrWhiteSpace(stateJson))
                return null;

            return JsonSerializer.Deserialize<HomeConsolePersistedState>(stateJson);
        }
        catch
        {
            return null;
        }
    }

    public async Task SaveAsync(HomeConsolePersistedState state)
    {
        try
        {
            var stateJson = JsonSerializer.Serialize(state);
            await _jsRuntime.InvokeVoidAsync("localStorage.setItem", StorageKey, stateJson);
        }
        catch
        {
            // Ignore persistence failures in the browser.
        }
    }
}
