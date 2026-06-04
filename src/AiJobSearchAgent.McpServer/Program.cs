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

    var allowedOrigins = (Environment.GetEnvironmentVariable("ALLOWED_ORIGINS") ?? "http://localhost:5173,http://localhost:5174")
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

    // Returns the cached result from the last search run without triggering a new one.
    // The frontend calls this on load; /api/jobs/search is only called on explicit refresh.
    app.MapGet("/api/jobs/results", async (JobSearchMcpService service, CancellationToken ct) =>
    {
        var response = await service.GetLatestOrSearchAsync(SearchJobsRequest.Default, ct);
        return Results.Ok(response);
    });

    app.MapGet("/api/jobs/search", async (JobSearchMcpService service, CancellationToken ct) =>
    {
        var response = await service.SearchJobsAsync(SearchJobsRequest.Default, ct);
        // Return full response so the dashboard can show source status and rejection summary
        return Results.Ok(response);
    });

    app.MapPost("/api/jobs/ingest_indeed", (IngestIndeedJobsRequest request, JobSearchMcpService service) =>
        Results.Ok(service.IngestIndeedJobs(request)));

    app.MapPost("/api/jobs/ingest_dice", (IngestDiceJobsRequest request, JobSearchMcpService service) =>
        Results.Ok(service.IngestDiceJobs(request)));

    app.MapPost("/api/jobs/ingest_ziprecruiter", (IngestZipRecruiterJobsRequest request, JobSearchMcpService service) =>
        Results.Ok(service.IngestZipRecruiterJobs(request)));

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

    app.MapGet("/api/cvs", () =>
    {
        var cvDirectory = EnsureCvDirectory();
        var files = Directory.EnumerateFiles(cvDirectory)
            .Where(IsAllowedCvFile)
            .Select(path => new CvFileDto(
                Path.GetFileName(path),
                new FileInfo(path).Length,
                File.GetLastWriteTimeUtc(path)))
            .OrderByDescending(file => file.UpdatedAtUtc)
            .ToArray();

        return Results.Ok(files);
    });

    app.MapPost("/api/cvs/upload", async (HttpRequest request, CancellationToken ct) =>
    {
        if (!request.HasFormContentType)
            return Results.BadRequest(new { message = "Upload must use multipart/form-data." });

        var form = await request.ReadFormAsync(ct);
        var file = form.Files["file"];
        if (file is null || file.Length == 0)
            return Results.BadRequest(new { message = "Select a CV file to upload." });

        var originalName = SanitiseCvFileName(file.FileName);
        if (!IsAllowedCvName(originalName))
            return Results.BadRequest(new { message = "Only .pdf, .docx, and .md files are allowed." });

        var mode = form["mode"].ToString();
        var requestedName = form["targetName"].ToString();
        var targetName = string.IsNullOrWhiteSpace(requestedName)
            ? originalName
            : SanitiseCvFileName(requestedName);

        if (!IsAllowedCvName(targetName))
            return Results.BadRequest(new { message = "New CV name must end with .pdf, .docx, or .md." });

        var cvDirectory = EnsureCvDirectory();
        var targetPath = Path.Combine(cvDirectory, targetName);
        if (File.Exists(targetPath) && !mode.Equals("replace", StringComparison.OrdinalIgnoreCase))
        {
            return Results.Conflict(new
            {
                message = "A CV with this name already exists.",
                existingName = targetName
            });
        }

        await using (var stream = File.Create(targetPath))
        {
            await file.CopyToAsync(stream, ct);
        }

        var info = new FileInfo(targetPath);
        return Results.Ok(new CvFileDto(info.Name, info.Length, info.LastWriteTimeUtc));
    });

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

static string EnsureCvDirectory()
{
    var repoRoot = FindRepoRoot() ?? Directory.GetCurrentDirectory();
    var cvDirectory = Path.Combine(repoRoot, "CVs");
    Directory.CreateDirectory(cvDirectory);
    return cvDirectory;
}

static string? FindRepoRoot()
{
    var dir = AppContext.BaseDirectory;
    for (var i = 0; i < 8; i++)
    {
        if (File.Exists(Path.Combine(dir, "AiJobSearchAgent.slnx"))) return dir;
        var parent = Directory.GetParent(dir);
        if (parent is null) break;
        dir = parent.FullName;
    }
    return null;
}

static string SanitiseCvFileName(string fileName)
{
    var name = Path.GetFileName(fileName).Trim();
    foreach (var invalid in Path.GetInvalidFileNameChars())
        name = name.Replace(invalid, '-');
    return name;
}

static bool IsAllowedCvFile(string path) =>
    IsAllowedCvName(Path.GetFileName(path));

static bool IsAllowedCvName(string name)
{
    var extension = Path.GetExtension(name);
    return extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".docx", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".md", StringComparison.OrdinalIgnoreCase);
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

public sealed record CvFileDto(string Name, long SizeBytes, DateTime UpdatedAtUtc);
