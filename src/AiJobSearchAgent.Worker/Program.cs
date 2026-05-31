using AiJobSearchAgent.Core;

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cts.Cancel();
};

if (args.Contains("--schedule", StringComparer.OrdinalIgnoreCase))
{
    await RunScheduleAsync(cts.Token);
    return;
}

await RunOnceAsync(cts.Token);

static async Task RunScheduleAsync(CancellationToken cancellationToken)
{
    var timeZone = FindUkTimeZone();
    while (!cancellationToken.IsCancellationRequested)
    {
        var nextRun = GetNextRun(DateTimeOffset.UtcNow, timeZone, new TimeOnly(10, 0));
        Console.WriteLine($"Next run: {TimeZoneInfo.ConvertTime(nextRun, timeZone):yyyy-MM-dd HH:mm zzz}");
        await Task.Delay(nextRun - DateTimeOffset.UtcNow, cancellationToken);
        await RunOnceAsync(cancellationToken);
    }
}

static async Task RunOnceAsync(CancellationToken cancellationToken)
{
    var timeZone = FindUkTimeZone();
    var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, timeZone).Date);
    var criteria = Defaults.CreateCriteria(today);
    var profile = Defaults.CreateCvProfile();
    var policies = Defaults.CreateSourcePolicies();
    var sources = SampleSources.Create(today);

    // Using a shared HttpClient for the fetcher
    using var httpClient = new HttpClient();
    var orchestrator = new JobSearchOrchestrator(
        sources,
        new SourcePolicyGuard(policies),
        new JobFilterEngine(),
        new CvMatchScorer(),
        new SimpleJobDescriptionFetcher(httpClient));

    var result = await orchestrator.RunAsync(criteria, profile, cancellationToken);
    var report = new MarkdownReportGenerator().Generate(result, criteria);

    var outputDirectory = Path.Combine(AppContext.BaseDirectory, "reports");
    Directory.CreateDirectory(outputDirectory);

    var reportPath = Path.Combine(outputDirectory, $"daily-job-matches-{criteria.PostedTo:yyyy-MM-dd}.md");
    await File.WriteAllTextAsync(reportPath, report, cancellationToken);

    Console.WriteLine($"Report generated: {reportPath}");
    Console.WriteLine($"Matches: {result.Matches.Count}");
}

static DateTimeOffset GetNextRun(DateTimeOffset utcNow, TimeZoneInfo timeZone, TimeOnly runAt)
{
    var localNow = TimeZoneInfo.ConvertTime(utcNow, timeZone);
    var localRun = localNow.Date.Add(runAt.ToTimeSpan());
    if (localRun <= localNow.DateTime)
    {
        localRun = localRun.AddDays(1);
    }

    return new DateTimeOffset(localRun, timeZone.GetUtcOffset(localRun)).ToUniversalTime();
}

static TimeZoneInfo FindUkTimeZone()
{
    try
    {
        return TimeZoneInfo.FindSystemTimeZoneById("Europe/London");
    }
    catch (TimeZoneNotFoundException)
    {
        return TimeZoneInfo.FindSystemTimeZoneById("GMT Standard Time");
    }
}
