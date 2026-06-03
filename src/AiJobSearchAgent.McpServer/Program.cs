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

    app.MapGet("/api/jobs/search", async (JobSearchMcpService service, CancellationToken ct) =>
    {
        var response = await service.SearchJobsAsync(SearchJobsRequest.Default, ct);
        return Results.Ok(response.Matches);
    });

    app.MapGet("/api/jobs/{source}/{sourceJobId}", async (string source, string sourceJobId, JobSearchMcpService service, CancellationToken ct) =>
    {
        var response = await service.GetJobAsync(source, sourceJobId, ct);
        return Results.Ok(response);
    });

    app.MapGet("/api/config", () =>
    {
        var envPath = Path.Combine(Directory.GetCurrentDirectory(), ".env");
        if (!File.Exists(envPath)) return Results.Ok(new Dictionary<string, string>());

        var lines = File.ReadAllLines(envPath);
        var config = lines
            .Where(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith("#"))
            .Select(line => line.Split('=', 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0].Trim(), parts => parts[1].Trim().Trim('"'));

        return Results.Ok(config);
    });

    app.MapPost("/api/config", async (Dictionary<string, string> newConfig) =>
    {
        var envPath = Path.Combine(Directory.GetCurrentDirectory(), ".env");
        var lines = File.Exists(envPath) ? File.ReadAllLines(envPath).ToList() : new List<string>();

        foreach (var kvp in newConfig)
        {
            var index = lines.FindIndex(l => l.StartsWith($"{kvp.Key}="));
            if (index >= 0)
            {
                lines[index] = $"{kvp.Key}=\"{kvp.Value}\"";
            }
            else
            {
                lines.Add($"{kvp.Key}=\"{kvp.Value}\"");
            }
        }

        await File.WriteAllLinesAsync(envPath, lines);
        return Results.NoContent();
    });

    await app.RunAsync();
}

static void RegisterShared(IServiceCollection services)
{
    services.AddSingleton(TimeProvider.System);
    services.AddHttpClient("Reed");
    services.AddSingleton<CredentialProvider>();
    services.AddSingleton<JobSearchMcpService>();
}
