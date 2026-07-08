using MineDash.Components;
using MineDash.Services;
using MineDash.Models;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Antiforgery;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Add session support
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(20);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
});

// Add HTTP context accessor for session access
builder.Services.AddHttpContextAccessor();

// our custom services
builder.Services.AddSingleton<IServerConfigStore, JsonServerConfigStore>();
builder.Services.AddSingleton<IDockerComposeImportService, DockerComposeImportService>();
builder.Services.AddScoped<ICommandStore, JsonCommandStore>();
builder.Services.AddSingleton<ITimedCommandStore, JsonTimedCommandStore>();
builder.Services.AddSingleton<IAppSettingsStore, JsonAppSettingsStore>();
builder.Services.AddScoped<IRconService, RconService>();
builder.Services.AddSingleton<ILogService, LogService>();
builder.Services.AddSingleton<ITimeDisplayService, TimeDisplayService>();
builder.Services.AddSingleton<IConsoleActivityStore, JsonConsoleActivityStore>();
builder.Services.AddSingleton<IConsoleLogFilterService, ConsoleLogFilterService>();
builder.Services.AddSingleton<IConsoleLogRetentionService, ConsoleLogRetentionService>();
builder.Services.AddSingleton<IConsoleTimelineService, ConsoleTimelineService>();
builder.Services.AddSingleton<IConsoleLogSessionService, ConsoleLogSessionService>();
builder.Services.AddSingleton<ILogPlayerHighlighter, LogPlayerHighlighter>();
builder.Services.AddScoped<IConsoleSessionPersistence, BrowserConsoleSessionPersistence>();
builder.Services.AddSingleton<IUserStore, JsonUserStore>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<AuthenticationStateProvider, SessionAuthenticationStateProvider>();

// Background service for timed commands
builder.Services.AddHostedService<TimedCommandScheduler>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();
app.UseSession();

// Add API endpoints
app.MapPost("/api/login", async (HttpRequest request, IAuthService authService, IUserStore userStore, IAntiforgery antiforgery) =>
{
    if (!request.HasFormContentType)
    {
        return Results.BadRequest("Request must be form data.");
    }

    // Validate antiforgery token
    try
    {
        await antiforgery.ValidateRequestAsync(request.HttpContext);
    }
    catch (AntiforgeryValidationException)
    {
        return Results.Redirect("/login?error=" + Uri.EscapeDataString("Invalid antiforgery token. Please try again."));
    }
    catch
    {
        return Results.Redirect("/login?error=" + Uri.EscapeDataString("Security validation failed. Please try again."));
    }

    var form = await request.ReadFormAsync();
    var username = form["Username"].ToString();
    var password = form["Password"].ToString();
    var confirmPassword = form["ConfirmPassword"].ToString();
    var isNewUserValue = form["IsNewUser"].ToString();
    var isNewUser = !string.IsNullOrWhiteSpace(isNewUserValue) && 
                    bool.TryParse(isNewUserValue, out var isNew) && isNew;

    if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
    {
        return Results.Redirect("/login?error=" + Uri.EscapeDataString("Username and password are required."));
    }

    var trimmedUsername = username.Trim();
    
    // Handle new user creation
    if (isNewUser)
    {
        if (password.Length < 6)
        {
            return Results.Redirect("/login?error=" + Uri.EscapeDataString("Password must be at least 6 characters long."));
        }

        if (password != confirmPassword)
        {
            return Results.Redirect("/login?error=" + Uri.EscapeDataString("Passwords do not match."));
        }

        var existingUser = await userStore.GetByUsernameAsync(trimmedUsername);
        if (existingUser != null)
        {
            return Results.Redirect("/login?error=" + Uri.EscapeDataString("Username already exists."));
        }

        var users = await userStore.GetAllAsync();
        var isFirstUser = users.Count == 0;

        var newUser = new User
        {
            Username = trimmedUsername,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            IsAdmin = isFirstUser
        };

        await userStore.AddOrUpdateAsync(newUser);
        await Task.Delay(100); // Ensure file is written

        var savedUser = await userStore.GetByUsernameAsync(trimmedUsername);
        if (savedUser == null)
        {
            return Results.Redirect("/login?error=" + Uri.EscapeDataString("Account created but could not be found. Please try logging in manually."));
        }

        // Login the new user
        var loginSuccess = await authService.LoginAsync(trimmedUsername, password);
        if (!loginSuccess)
        {
            return Results.Redirect("/login?error=" + Uri.EscapeDataString("Account created but login failed. Please try logging in manually."));
        }
    }
    else
    {
        // Login existing user
        var loginSuccess = await authService.LoginAsync(trimmedUsername, password);
        if (!loginSuccess)
        {
            return Results.Redirect("/login?error=" + Uri.EscapeDataString("Invalid username or password."));
        }
    }

    // Redirect to home page after successful login
    return Results.Redirect("/");
})
.WithName("Login");

// Add logout endpoint
app.MapPost("/api/logout", async (HttpRequest request, IAuthService authService, IAntiforgery antiforgery) =>
{
    // Validate antiforgery token
    try
    {
        await antiforgery.ValidateRequestAsync(request.HttpContext);
    }
    catch
    {
        // Even if token validation fails, allow logout for security
        // (user wants to log out, so let them)
    }

    await authService.LogoutAsync();
    return Results.Redirect("/login");
})
.WithName("Logout");

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();