using MineDash.Models;

namespace MineDash.Services;

public interface IAuthService
{
    Task<bool> LoginAsync(string username, string password);
    Task LogoutAsync();
    Task<User?> GetCurrentUserAsync();
    User? GetCurrentUser(); // Synchronous version - may return null if session not loaded
    Task<bool> IsAuthenticatedAsync();
    bool IsAuthenticated(); // Synchronous version - may return false if session not loaded
    Task<bool> IsAdminAsync();
    bool IsAdmin(); // Synchronous version - may return false if session not loaded
    Task<bool> ChangePasswordAsync(string userId, string currentPassword, string newPassword);
    void NotifyAuthenticationStateChanged();
}

