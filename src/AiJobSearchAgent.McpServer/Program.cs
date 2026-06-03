using AiJobSearchAgent.Core;
using AiJobSearchAgent.McpServer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Builder;

// --http flag: start the HTTP API for the frontend dashboard (no MCP STDIO transport).
// Default (no flag): run as a local STDIO MCP server for AI clients.
var isHttpMode = args.Contains("--http", StringComparer.OrdinalIgnoreCase);

if (isHttpMode)
{
    await RunHttpAsync(args);
}
else
{
    await RunStdioAsync(args);
}

static async Task RunStdioAsync(string[] args)
{
    var builder = Host.CreateApplicationBuilder(args);
    // Suppress all console logging — stdout carries MCP protocol messages and must stay clean.
    builder.Logging.ClearProviders();
    RegisterShared(builder.Services);
    builder.Services.AddSingleton<McpJobSearchTools>();
    builder.Services
        .AddMcpServer()
        .WithStdioServerTransport()
        .WithTools<McpJobSearchTools>();
    await builder.Build().RunAsync();
}

static async Task RunHttpAsync(string[] args)
{
    var builder = WebApplication.CreateBuilder(args);

    var port = Environment.GetEnvironmentVariable("PORT") ?? "5005";
    builder.WebHost.UseUrls($"http://*:{port}");

    RegisterShared(builder.Services);

    var allowedOrigins = (Environment.GetEnvironmentVariable("ALLOWED_ORIGINS") ?? "http://localhost:5173")
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    builder.Services.AddCors(options =>
        options.AddPolicy("Frontend", policy =>
            policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));

    var app = builder.Build();

    // Force the minimal API to listen on localhost:5001 to avoid conflicts with system services on :5000
    app.Urls.Add("http://localhost:5001");
    app.UseCors("Frontend");

    // Ensure the app_settings table exists (idempotent, safe to run on every start)
    var settings = app.Services.GetRequiredService<SettingsRepository>();
    await settings.EnsureSchemaAsync();
    await ApplySettingsToEnvironmentAsync(settings, CancellationToken.None);

    app.MapGet("/api/jobs/search", async (JobSearchMcpService service, CancellationToken ct) =>
    {
        var response = await service.SearchJobsAsync(SearchJobsRequest.Default, ct);
        // Return full response so the dashboard can show source status and rejection summary
        return Results.Ok(response);
    });

    app.MapGet("/api/jobs/{source}/{sourceJobId}", async (string source, string sourceJobId, JobSearchMcpService service, CancellationToken ct) =>
    {
        var response = await service.GetJobAsync(source, sourceJobId, ct);
        return Results.Ok(response);
    });

    app.MapGet("/api/config", async (SettingsRepository db, CancellationToken ct) =>
    {
        var result = new Dictionary<string, string>(SettingsRepository.DefaultValues, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in ReadEnvFile())
        {
            if (SettingsRepository.ConfigurableKeys.Contains(key))
                result[key] = value;
        }

        var dbSettings = await db.GetAllAsync(ct);
        foreach (var (k, v) in dbSettings)
        {
            if (SettingsRepository.ConfigurableKeys.Contains(k))
                result[k] = v;
        }

        var configuredSecretKeys = result
            .Where(kv => SettingsRepository.SecretKeys.Contains(kv.Key) && !string.IsNullOrWhiteSpace(kv.Value))
            .Select(kv => kv.Key)
            .ToArray();

        foreach (var key in SettingsRepository.SecretKeys)
        {
            if (result.ContainsKey(key))
                result[key] = string.Empty;
        }

        result["__secretKeys"] = string.Join(",", SettingsRepository.SecretKeys);
        result["__configuredSecretKeys"] = string.Join(",", configuredSecretKeys);
        return Results.Ok(result);
    });

    app.MapPost("/api/config", async (Dictionary<string, string> newConfig, SettingsRepository db, CancellationToken ct) =>
    {
        var settingsToSave = newConfig
            .Where(kv => SettingsRepository.ConfigurableKeys.Contains(kv.Key))
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);

        await db.SaveAsync(settingsToSave, ct);
        foreach (var (key, value) in settingsToSave)
        {
            if (SettingsRepository.SecretKeys.Contains(key) && string.IsNullOrEmpty(value)) continue;
            Environment.SetEnvironmentVariable(key, value);
        }

        return Results.NoContent();
    });

    app.MapGet("/api/config/meta", () =>
        Results.Ok(new { secretKeys = SettingsRepository.SecretKeys, dbAvailable = app.Services.GetRequiredService<SettingsRepository>().IsAvailable }));

    await app.RunAsync();
}

static Dictionary<string, string> ReadEnvFile()
{
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    var envPath = FindEnvPath();
    if (!File.Exists(envPath)) return result;

    foreach (var line in File.ReadAllLines(envPath))
    {
        var trimmed = line.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#')) continue;
        var idx = trimmed.IndexOf('=');
        if (idx <= 0) continue;
        var key = trimmed[..idx].Trim();
        var raw = trimmed[(idx + 1)..].Trim();
        var value = raw.Length >= 2 && ((raw[0] == '"' && raw[^1] == '"') || (raw[0] == '\'' && raw[^1] == '\''))
            ? raw[1..^1] : raw;
        result[key] = value;
    }
    return result;
}

static async Task ApplySettingsToEnvironmentAsync(SettingsRepository settings, CancellationToken ct)
{
    var saved = await settings.GetAllAsync(ct);
    foreach (var (key, value) in saved)
    {
        if (SettingsRepository.ConfigurableKeys.Contains(key))
            Environment.SetEnvironmentVariable(key, value);
    }
}

static string? FindEnvPath()
{
    var dir = AppContext.BaseDirectory;
    for (var i = 0; i < 8; i++)
    {
        var candidate = Path.Combine(dir, ".env");
        if (File.Exists(candidate)) return candidate;
        var parent = Directory.GetParent(dir);
        if (parent is null) break;
        dir = parent.FullName;
    }
    return null;
}

static void RegisterShared(IServiceCollection services)
{
    services.AddSingleton(TimeProvider.System);
    services.AddHttpClient("Reed");
    services.AddSingleton<CredentialProvider>();
    services.AddSingleton<JobSearchMcpService>();
    services.AddSingleton(sp =>
    {
        var connectionString = Environment.GetEnvironmentVariable("DATABASE_URL");
        var encryptionKey    = Environment.GetEnvironmentVariable("SETTINGS_ENCRYPTION_KEY");
        return new SettingsRepository(connectionString, encryptionKey);
    });
}
