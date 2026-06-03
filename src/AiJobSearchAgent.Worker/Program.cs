using AiJobSearchAgent.Core;

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cts.Cancel();
};

// Load .env file so the worker picks up secrets without needing shell exports
LoadDotEnv();

if (args.Contains("--schedule", StringComparer.OrdinalIgnoreCase))
{
    await RunScheduleAsync(cts.Token);
    return;
}

await RunOnceAsync(cts.Token);

// ── scheduler ──────────────────────────────────────────────────────────────────

static async Task RunScheduleAsync(CancellationToken cancellationToken)
{
    var timeZone = FindTimeZone();
    var runAt = GetRunAt();
    while (!cancellationToken.IsCancellationRequested)
    {
        var nextRun = GetNextRun(DateTimeOffset.UtcNow, timeZone, runAt);
        Console.WriteLine($"Next run: {TimeZoneInfo.ConvertTime(nextRun, timeZone):yyyy-MM-dd HH:mm zzz}");
        await Task.Delay(nextRun - DateTimeOffset.UtcNow, cancellationToken);
        await RunOnceAsync(cancellationToken);
    }
}

// ── single run ─────────────────────────────────────────────────────────────────

static async Task RunOnceAsync(CancellationToken cancellationToken)
{
    var timeZone = FindTimeZone();
    var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, timeZone).Date);
    var criteria = Defaults.CreateCriteria(today);
    var profile = Defaults.CreateCvProfile();

    var reedKey     = GetSecret("REED_API_KEY");
    var gmailCreds  = GetSecret("GMAIL_CREDENTIALS_JSON");
    var gmailQuery  = GetSecret("GMAIL_SEARCH_QUERY")        ?? "label:job-alerts is:unread";
    var indeedQuery = GetSecret("INDEED_GMAIL_SEARCH_QUERY") ?? "from:jobalerts-noreply@indeed.com is:unread";
    var slackUrl    = GetSecret("SLACK_WEBHOOK_URL");

    // ── adapters ──────────────────────────────────────────────────────────────
    var sources = new List<IJobSourceAdapter>();

    if (!string.IsNullOrWhiteSpace(reedKey))
    {
        sources.Add(new ReedApiJobSourceAdapter(new HttpClient(), reedKey));
        Console.WriteLine("[source] Reed API: enabled");
    }
    else
    {
        Console.WriteLine("[source] Reed API: skipped (REED_API_KEY not set)");
    }

    if (!string.IsNullOrWhiteSpace(gmailCreds))
    {
        sources.Add(new GmailAlertJobSourceAdapter(gmailCreds, gmailQuery));
        Console.WriteLine("[source] Gmail Alerts: enabled");

        sources.Add(new IndeedAlertJobSourceAdapter(gmailCreds, indeedQuery));
        Console.WriteLine("[source] Indeed UK (alert emails): enabled");
    }
    else
    {
        Console.WriteLine("[source] Gmail Alerts: skipped (GMAIL_CREDENTIALS_JSON not set)");
        Console.WriteLine("[source] Indeed UK:    skipped (GMAIL_CREDENTIALS_JSON not set)");
    }

    if (sources.Count == 0)
    {
        Console.WriteLine("[warn] No live sources configured — falling back to sample data.");
        Console.WriteLine("[warn] Set REED_API_KEY and/or GMAIL_CREDENTIALS_JSON in .env to use real data.");
        sources.AddRange(SampleSources.Create(today));
    }

    // ── source policies ───────────────────────────────────────────────────────
    var policies = new List<SourcePolicy>
    {
        new("Reed",         FetchMode.ApprovedApi, !string.IsNullOrWhiteSpace(reedKey),    TimeSpan.FromSeconds(3),  new DateOnly(2026, 6, 3)),
        new("Gmail Alerts", FetchMode.AlertInbox,  !string.IsNullOrWhiteSpace(gmailCreds), TimeSpan.FromSeconds(0),  new DateOnly(2026, 6, 3)),
        new("Indeed UK",    FetchMode.AlertInbox,  !string.IsNullOrWhiteSpace(gmailCreds), TimeSpan.FromSeconds(10), new DateOnly(2026, 6, 3)),
        // Sample fallback sources — always enabled as a catch-all (ignored unless sources list contains them)
        new("JobServe",     FetchMode.AlertInbox,  true, TimeSpan.FromSeconds(0), new DateOnly(2026, 6, 3)),
    };

    // ── run ───────────────────────────────────────────────────────────────────
    var result = await new JobSearchOrchestrator(
        sources,
        new SourcePolicyGuard(policies),
        new JobFilterEngine(),
        new CvMatchScorer()).RunAsync(criteria, profile, cancellationToken);

    // ── write report ──────────────────────────────────────────────────────────
    var repoRoot = FindRepoRoot() ?? Directory.GetCurrentDirectory();
    var reportsDir = Path.Combine(repoRoot, "reports");

    using var httpClient = new HttpClient();
    IJobReporter[] reporters =
    [
        new MarkdownFileJobReporter(reportsDir),
        new SlackJobReporter(httpClient, slackUrl)
    ];

    foreach (var reporter in reporters)
        await reporter.ReportAsync(result, criteria, cancellationToken);

    Console.WriteLine($"Matches: {result.Matches.Count}");
    Console.WriteLine($"Report:  {reportsDir}/daily-job-matches-{today:yyyy-MM-dd}.md");
    Console.WriteLine();

    foreach (var match in result.Matches.OrderByDescending(m => m.Score))
    {
        var comp = match.Job.EmploymentType == EmploymentType.Permanent
            ? $"£{match.Job.SalaryMin:0}-{match.Job.SalaryMax:0}/yr"
            : $"£{match.Job.DayRateMin:0}-{match.Job.DayRateMax:0}/day";
        Console.WriteLine($"  [{match.Score,3}] {match.Job.Title} @ {match.Job.Company}  |  {comp}  |  {match.Job.Source}");
        Console.WriteLine($"       {match.Job.Url}");
    }
}

// ── .env loader ────────────────────────────────────────────────────────────────

static void LoadDotEnv()
{
    var path = FindEnvPath();
    if (path is null) return;

    foreach (var line in File.ReadAllLines(path))
    {
        var trimmed = line.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#')) continue;

        var idx = trimmed.IndexOf('=');
        if (idx <= 0) continue;

        var key = trimmed[..idx].Trim();

        // Real environment variables always win over .env
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(key))) continue;

        var raw = trimmed[(idx + 1)..].Trim();
        // Strip one wrapping quote pair — handles "value" and 'value' (bash-sourced .env)
        var value = raw.Length >= 2 && ((raw[0] == '"' && raw[^1] == '"') || (raw[0] == '\'' && raw[^1] == '\''))
            ? raw[1..^1]
            : raw;

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

static string? GetSecret(string name) => Environment.GetEnvironmentVariable(name);

// ── time helpers ───────────────────────────────────────────────────────────────

static DateTimeOffset GetNextRun(DateTimeOffset utcNow, TimeZoneInfo timeZone, TimeOnly runAt)
{
    var localNow = TimeZoneInfo.ConvertTime(utcNow, timeZone);
    var localRun = localNow.Date.Add(runAt.ToTimeSpan());
    if (localRun <= localNow.DateTime) localRun = localRun.AddDays(1);
    return new DateTimeOffset(localRun, timeZone.GetUtcOffset(localRun)).ToUniversalTime();
}

static TimeOnly GetRunAt()
{
    var configured = Environment.GetEnvironmentVariable("JOB_SEARCH_RUN_AT");
    return TimeOnly.TryParse(configured, out var runAt) ? runAt : new TimeOnly(10, 0);
}

static TimeZoneInfo FindTimeZone()
{
    var configured = Environment.GetEnvironmentVariable("JOB_SEARCH_TIME_ZONE");
    var preferred = string.IsNullOrWhiteSpace(configured) ? "Europe/London" : configured;
    try { return TimeZoneInfo.FindSystemTimeZoneById(preferred); }
    catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("GMT Standard Time"); }
}
