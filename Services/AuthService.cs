using MineDash.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Components.Authorization;

namespace MineDash.Services;

public class AuthService : IAuthService
{
    private readonly IUserStore _userStore;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private const string SessionKeyUserId = "UserId";
    private const string SessionKeyUsername = "Username";
    private const string SessionKeyIsAdmin = "IsAdmin";

    public AuthService(IUserStore userStore, IHttpContextAccessor httpContextAccessor)
    {
        _userStore = userStore;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<bool> LoginAsync(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            System.Diagnostics.Debug.WriteLine($"LoginAsync: Username or password is empty");
            return false;
        }

        var trimmedUsername = username.Trim();
        // Don't trim password - passwords can legitimately have leading/trailing spaces
        // But ensure it's not null
        var passwordToVerify = password ?? string.Empty;
        
        var user = await _userStore.GetByUsernameAsync(trimmedUsername);
        if (user == null)
        {
            System.Diagnostics.Debug.WriteLine($"LoginAsync: User not found for username: '{trimmedUsername}'");
            return false;
        }

        if (string.IsNullOrWhiteSpace(user.PasswordHash))
        {
            System.Diagnostics.Debug.WriteLine($"LoginAsync: User has no password hash stored");
            return false;
        }

        System.Diagnostics.Debug.WriteLine($"LoginAsync: User found: '{user.Username}', Hash length: {user.PasswordHash.Length}, Password length: {passwordToVerify.Length}, Hash starts with: {user.PasswordHash.Substring(0, Math.Min(10, user.PasswordHash.Length))}");

        // Verify password using BCrypt
        bool passwordValid;
        try
        {
            passwordValid = BCrypt.Net.BCrypt.Verify(passwordToVerify, user.PasswordHash);
            System.Diagnostics.Debug.WriteLine($"LoginAsync: Password verification result: {passwordValid}");
            
            // If verification failed, try with trimmed password (in case user accidentally added spaces)
            if (!passwordValid && passwordToVerify != passwordToVerify.Trim())
            {
                System.Diagnostics.Debug.WriteLine($"LoginAsync: Retrying with trimmed password");
                passwordValid = BCrypt.Net.BCrypt.Verify(passwordToVerify.Trim(), user.PasswordHash);
                System.Diagnostics.Debug.WriteLine($"LoginAsync: Trimmed password verification result: {passwordValid}");
            }
        }
        catch (Exception ex)
        {
            // If BCrypt throws an exception (e.g., invalid hash format), log and return false
            System.Diagnostics.Debug.WriteLine($"BCrypt verification error: {ex.Message}, StackTrace: {ex.StackTrace}");
            return false;
        }

        if (!passwordValid)
        {
            System.Diagnostics.Debug.WriteLine($"LoginAsync: Password verification failed after all attempts");
            return false;
        }

        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null)
            return false;

        // Ensure session is loaded before writing to it
        await httpContext.Session.LoadAsync();

        // In Blazor Server, we need to write to session before response starts
        // Try to commit the session - if it fails, we need to handle it
        try
        {
            httpContext.Session.SetString(SessionKeyUserId, user.Id);
            httpContext.Session.SetString(SessionKeyUsername, user.Username);
            httpContext.Session.SetString(SessionKeyIsAdmin, user.IsAdmin.ToString());

            // Commit the session changes
            await httpContext.Session.CommitAsync();
            System.Diagnostics.Debug.WriteLine($"LoginAsync: Session written successfully for user: {user.Username}");
        }
        catch (InvalidOperationException ex)
        {
            // If session can't be written (response started), we need to handle this
            // In Blazor Server InteractiveServer, the response may have already started
            // Try to reload and commit again
            System.Diagnostics.Debug.WriteLine($"LoginAsync: Session write failed, attempting reload: {ex.Message}");
            try
            {
                await httpContext.Session.LoadAsync();
                httpContext.Session.SetString(SessionKeyUserId, user.Id);
                httpContext.Session.SetString(SessionKeyUsername, user.Username);
                httpContext.Session.SetString(SessionKeyIsAdmin, user.IsAdmin.ToString());
                await httpContext.Session.CommitAsync();
                System.Diagnostics.Debug.WriteLine($"LoginAsync: Session written successfully after reload for user: {user.Username}");
            }
            catch
            {
                // If it still fails, log but continue - the session will be available on next request
                System.Diagnostics.Debug.WriteLine($"LoginAsync: Session write failed after reload, will be available on next request");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"LoginAsync: Unexpected session error: {ex.Message}");
            // Continue - session might still be written
        }

        return true;
    }

    public void NotifyAuthenticationStateChanged()
    {
        // This method exists for interface compatibility
        // The actual notification is done by SessionAuthenticationStateProvider
    }

    public async Task LogoutAsync()
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext != null)
        {
            try
            {
                await httpContext.Session.LoadAsync();
                httpContext.Session.Clear();
                await httpContext.Session.CommitAsync();
                System.Diagnostics.Debug.WriteLine("LogoutAsync: Session cleared successfully");
            }
            catch (Exception ex)
            {
                // Even if commit fails, try to clear the session
                System.Diagnostics.Debug.WriteLine($"LogoutAsync: Session clear error: {ex.Message}");
                try
                {
                    httpContext.Session.Clear();
                }
                catch
                {
                    // Ignore - session might already be cleared
                }
            }
        }
    }

    public async Task<User?> GetCurrentUserAsync()
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null)
            return null;

        // Ensure session is loaded before reading from it
        await httpContext.Session.LoadAsync();

        var userId = httpContext.Session.GetString(SessionKeyUserId);
        if (string.IsNullOrEmpty(userId))
            return null;

        // We could fetch from store, but for performance, we'll use session data
        // If we need full user object, we'd fetch from store
        var username = httpContext.Session.GetString(SessionKeyUsername);
        var isAdminStr = httpContext.Session.GetString(SessionKeyIsAdmin);
        
        if (string.IsNullOrEmpty(username))
            return null;

        return new User
        {
            Id = userId,
            Username = username,
            IsAdmin = bool.TryParse(isAdminStr, out var isAdmin) && isAdmin
        };
    }

    public User? GetCurrentUser()
    {
        // Synchronous version - try to read without loading (may fail if session not loaded)
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null)
            return null;

        var userId = httpContext.Session.GetString(SessionKeyUserId);
        if (string.IsNullOrEmpty(userId))
            return null;

        var username = httpContext.Session.GetString(SessionKeyUsername);
        var isAdminStr = httpContext.Session.GetString(SessionKeyIsAdmin);
        
        if (string.IsNullOrEmpty(username))
            return null;

        return new User
        {
            Id = userId,
            Username = username,
            IsAdmin = bool.TryParse(isAdminStr, out var isAdmin) && isAdmin
        };
    }

    public async Task<bool> IsAuthenticatedAsync()
    {
        var user = await GetCurrentUserAsync();
        return user != null;
    }

    public bool IsAuthenticated()
    {
        return GetCurrentUser() != null;
    }

    public async Task<bool> IsAdminAsync()
    {
        var user = await GetCurrentUserAsync();
        return user?.IsAdmin ?? false;
    }

    public bool IsAdmin()
    {
        var user = GetCurrentUser();
        return user?.IsAdmin ?? false;
    }

    public async Task<bool> ChangePasswordAsync(string userId, string currentPassword, string newPassword)
    {
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 6)
            return false;

        var user = await _userStore.GetByIdAsync(userId);
        if (user == null)
            return false;

        if (!BCrypt.Net.BCrypt.Verify(currentPassword, user.PasswordHash))
            return false;

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        await _userStore.AddOrUpdateAsync(user);

        return true;
    }
}

