using MineDash.Components;
using MineDash.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// our custom services
builder.Services.AddSingleton<IServerConfigStore, JsonServerConfigStore>();
builder.Services.AddSingleton<ICommandStore, JsonCommandStore>();
builder.Services.AddScoped<IRconService, RconService>();

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

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();