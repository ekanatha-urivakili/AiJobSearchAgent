using AiJobSearchAgent.Core;
using AiJobSearchAgent.McpServer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Builder;
using System.Security.Cryptography;

// --http flag: start the HTTP API for the frontend dashboard (no MCP STDIO transport).
// Default (no flag): run as a local STDIO MCP server for AI clients.
var isHttpMode = args.Contains("--http", StringComparer.OrdinalIgnoreCase);
const long MaxCvUploadMegabytes = 5;
const long MaxCvUploadBytes = MaxCvUploadMegabytes * 1024 * 1024;

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

    var port = Environment.GetEnvironmentVariable("PORT") ?? "5001";
    var host = Environment.GetEnvironmentVariable("JOB_AGENT_HTTP_HOST") ?? "localhost";
    builder.WebHost.UseUrls($"http://{host}:{port}");

    RegisterShared(builder.Services);

    var allowedOrigins = (Environment.GetEnvironmentVariable("ALLOWED_ORIGINS") ?? "http://localhost:5173,http://localhost:5174")
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    builder.Services.AddCors(options =>
        options.AddPolicy("Frontend", policy =>
            policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));

    var app = builder.Build();

    app.UseCors("Frontend");

    app.Use(async (context, next) =>
    {
        if (context.Request.Path.StartsWithSegments("/api") && !IsApiAuthorized(context))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { message = "Unauthorized." });
            return;
        }

        await next();
    });

    // Ensure the app_settings table exists (idempotent, safe to run on every start)
    var settings = app.Services.GetRequiredService<SettingsRepository>();
    await settings.EnsureSchemaAsync();
    var jobRuns = app.Services.GetRequiredService<JobRunRepository>();
    await jobRuns.EnsureSchemaAsync();
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

    app.MapGet("/api/jobs/{source}/{sourceJobId}/application", async (string source, string sourceJobId, JobRunRepository db, CancellationToken ct) =>
    {
        var response = await db.GetApplicationAsync(source, sourceJobId, ct)
            ?? new JobApplicationDto(source, sourceJobId, "New", string.Empty, DateTimeOffset.UtcNow);
        return Results.Ok(response);
    });

    app.MapPost("/api/jobs/{source}/{sourceJobId}/application", async (string source, string sourceJobId, SaveJobApplicationRequest request, JobRunRepository db, CancellationToken ct) =>
    {
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "New", "Interested", "Applied", "FollowUp", "Interview", "Rejected", "Offer"
        };

        if (!allowed.Contains(request.Status))
            return Results.BadRequest(new { message = "Unknown application status." });

        var response = await db.SaveApplicationAsync(source, sourceJobId, request.Status, request.Notes, ct);
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

        if (file.Length > MaxCvUploadBytes)
            return Results.BadRequest(new { message = $"CV must be {MaxCvUploadMegabytes} MB or smaller." });

        var originalName = SanitiseCvFileName(file.FileName);
        if (!IsAllowedCvName(originalName))
            return Results.BadRequest(new { message = "Only .pdf, .docx, and .md files are allowed." });

        if (!await HasAllowedCvContentAsync(file, originalName, ct))
            return Results.BadRequest(new { message = "CV content does not match the file extension." });

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

static bool IsApiAuthorized(HttpContext context)
{
    var expected = Environment.GetEnvironmentVariable("JOB_AGENT_API_KEY")
        ?? ReadEnvFile().GetValueOrDefault("JOB_AGENT_API_KEY");
    if (string.IsNullOrWhiteSpace(expected))
        return IsLoopback(context.Connection.RemoteIpAddress);

    var supplied = context.Request.Headers["X-Job-Agent-Key"].FirstOrDefault();
    if (string.IsNullOrWhiteSpace(supplied))
    {
        var auth = context.Request.Headers.Authorization.FirstOrDefault();
        const string bearerPrefix = "Bearer ";
        if (auth?.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase) == true)
            supplied = auth[bearerPrefix.Length..];
    }

    return FixedTimeEquals(expected, supplied);
}

static bool IsLoopback(System.Net.IPAddress? address) =>
    address is null || System.Net.IPAddress.IsLoopback(address);

static bool FixedTimeEquals(string expected, string? supplied)
{
    if (string.IsNullOrEmpty(supplied)) return false;
    var expectedBytes = System.Text.Encoding.UTF8.GetBytes(expected);
    var suppliedBytes = System.Text.Encoding.UTF8.GetBytes(supplied);
    return expectedBytes.Length == suppliedBytes.Length
        && CryptographicOperations.FixedTimeEquals(expectedBytes, suppliedBytes);
}

static async Task<bool> HasAllowedCvContentAsync(IFormFile file, string fileName, CancellationToken ct)
{
    var extension = Path.GetExtension(fileName);
    await using var stream = file.OpenReadStream();
    var buffer = new byte[Math.Min(512, (int)file.Length)];
    var read = await stream.ReadAsync(buffer, ct);
    var header = buffer.AsSpan(0, read);

    if (extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        return header.StartsWith("%PDF-"u8);

    if (extension.Equals(".docx", StringComparison.OrdinalIgnoreCase))
        return header.StartsWith("PK"u8);

    return extension.Equals(".md", StringComparison.OrdinalIgnoreCase)
        && !header.Contains((byte)0);
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
    services.AddSingleton(sp =>
    {
        var connectionString = Environment.GetEnvironmentVariable("DATABASE_URL");
        return new JobRunRepository(connectionString);
    });
}

public sealed record CvFileDto(string Name, long SizeBytes, DateTime UpdatedAtUtc);
