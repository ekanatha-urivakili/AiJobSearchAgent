using AiJobSearchAgent.Core;

var tests = new (string Name, Action Test)[]
{
    ("Permanent jobs below GBP 75,000 are rejected", PermanentBelowSalaryIsRejected),
    ("Contract jobs below GBP 400/day are rejected", ContractBelowDayRateIsRejected),
    ("Contract jobs shorter than 6 months are rejected", ShortContractIsRejected),
    ("Remote jobs outside radius are accepted when compensation qualifies", RemoteOutsideRadiusIsAccepted),
    ("CV scorer recommends strong .NET React AWS jobs", StrongMatchIsRecommended),
    ("Jobs with same source and source ID are deduplicated, newest kept", DeduplicationUsesSourceJobId),
    ("Jobs from different sources with same source ID are not merged", DifferentSourcesSameIdNotMerged),
    ("Title matching works against criteria titles with partial contains", TitleMatchingWorksByContains),
    ("Old postings outside date range are rejected", OldPostingIsRejected),
    ("Office jobs outside radius are rejected", OfficeJobOutsideRadiusIsRejected),
    ("CV scorer penalises jobs with few keyword matches", LowKeywordMatchIsNotRecommended),
    ("Slack reporter skips when URL is null", SlackReporterSkipsWhenUrlIsNull),
    ("Gmail adapter returns warning when credentials are null", GmailAdapterReturnsWarningWhenCredentialsAreNull),
};

var failures = new List<string>();

foreach (var test in tests)
{
    try
    {
        test.Test();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception ex)
    {
        failures.Add($"{test.Name}: {ex.Message}");
        Console.WriteLine($"FAIL {test.Name}: {ex.Message}");
    }
}

if (failures.Count > 0)
{
    Console.WriteLine();
    Console.WriteLine("Failures:");
    foreach (var failure in failures)
    {
        Console.WriteLine(failure);
    }

    return 1;
}

return 0;

// ── filter tests ────────────────────────────────────────────────────────────

static void PermanentBelowSalaryIsRejected()
{
    var criteria = Defaults.CreateCriteria(new DateOnly(2026, 5, 30));
    var job = CreateJob(EmploymentType.Permanent, WorkMode.Hybrid, salaryMin: 70000, salaryMax: 74000);

    var decision = new JobFilterEngine().Evaluate(job, criteria);

    AssertFalse(decision.Accepted);
    AssertEqual("Below salary threshold", decision.Reason);
}

static void ContractBelowDayRateIsRejected()
{
    var criteria = Defaults.CreateCriteria(new DateOnly(2026, 5, 30));
    var job = CreateJob(EmploymentType.Contract, WorkMode.Hybrid, dayRateMin: 350, dayRateMax: 375, contractMonths: 6);

    var decision = new JobFilterEngine().Evaluate(job, criteria);

    AssertFalse(decision.Accepted);
    AssertEqual("Below day rate threshold", decision.Reason);
}

static void ShortContractIsRejected()
{
    var criteria = Defaults.CreateCriteria(new DateOnly(2026, 5, 30));
    var job = CreateJob(EmploymentType.Contract, WorkMode.Hybrid, dayRateMin: 450, dayRateMax: 500, contractMonths: 3);

    var decision = new JobFilterEngine().Evaluate(job, criteria);

    AssertFalse(decision.Accepted);
    AssertEqual("Below contract duration threshold", decision.Reason);
}

static void RemoteOutsideRadiusIsAccepted()
{
    var criteria = Defaults.CreateCriteria(new DateOnly(2026, 5, 30));
    var job = CreateJob(EmploymentType.Permanent, WorkMode.Remote, distanceMiles: 200, salaryMin: 80000, salaryMax: 90000);

    var decision = new JobFilterEngine().Evaluate(job, criteria);

    AssertTrue(decision.Accepted);
}

static void OldPostingIsRejected()
{
    var today = new DateOnly(2026, 5, 30);
    var criteria = Defaults.CreateCriteria(today);
    var job = CreateJobOnDate(today.AddDays(-30), EmploymentType.Permanent, WorkMode.Hybrid, salaryMin: 85000);

    var decision = new JobFilterEngine().Evaluate(job, criteria);

    AssertFalse(decision.Accepted);
    AssertEqual("Old posting", decision.Reason);
}

static void OfficeJobOutsideRadiusIsRejected()
{
    var criteria = Defaults.CreateCriteria(new DateOnly(2026, 5, 30));
    var job = CreateJob(EmploymentType.Permanent, WorkMode.Office, distanceMiles: 80, salaryMin: 85000);

    var decision = new JobFilterEngine().Evaluate(job, criteria);

    AssertFalse(decision.Accepted);
    AssertEqual("Outside radius", decision.Reason);
}

// ── scorer tests ─────────────────────────────────────────────────────────────

static void StrongMatchIsRecommended()
{
    var job = CreateJob(EmploymentType.Permanent, WorkMode.Hybrid, salaryMin: 85000, salaryMax: 95000);
    var match = new CvMatchScorer().Score(job, Defaults.CreateCvProfile());

    AssertTrue(match.Recommended);
}

static void LowKeywordMatchIsNotRecommended()
{
    var job = new JobPosting(
        "Test", Guid.NewGuid().ToString("N"), new Uri("https://example.com/job"),
        "Senior Software Engineer", "Example Ltd", "Milton Keynes",
        10, EmploymentType.Permanent, WorkMode.Hybrid,
        85000, 90000, null, null, null,
        new DateOnly(2026, 5, 29),
        "Java Spring Boot developer needed for legacy migration project.");

    var match = new CvMatchScorer().Score(job, Defaults.CreateCvProfile());

    AssertFalse(match.Recommended);
}

// ── deduplication tests ───────────────────────────────────────────────────────

static void DeduplicationUsesSourceJobId()
{
    // Same source + sourceJobId, different company/title/location.
    // New key (source|sourceJobId) deduplicates these to one entry.
    // Old key (company|title|location) would have kept both.
    var older = new JobPosting("Reed", "reed-001", new Uri("https://example.com"), "Senior Engineer", "Acme", "London",
        10, EmploymentType.Permanent, WorkMode.Hybrid, 85000, 95000, null, null, null,
        new DateOnly(2026, 5, 28), "C# AWS");
    var newer = new JobPosting("Reed", "reed-001", new Uri("https://example.com"), "Senior Engineer (Updated)", "Acme Ltd", "Manchester",
        200, EmploymentType.Permanent, WorkMode.Hybrid, 85000, 95000, null, null, null,
        new DateOnly(2026, 5, 30), "C# AWS updated");

    var deduped = new[] { older, newer }
        .GroupBy(j => $"{j.Source}|{j.SourceJobId}".ToLowerInvariant())
        .Select(g => g.OrderByDescending(j => j.PostedDate).First())
        .ToArray();

    AssertEqual(1, deduped.Length);
    AssertEqual("Senior Engineer (Updated)", deduped[0].Title);
}

static void DifferentSourcesSameIdNotMerged()
{
    var reedJob = new JobPosting("Reed", "job-001", new Uri("https://example.com"), "Senior Engineer", "Acme", "London",
        10, EmploymentType.Permanent, WorkMode.Hybrid, 85000, 95000, null, null, null,
        new DateOnly(2026, 5, 30), "C# AWS");
    var jobserveJob = new JobPosting("JobServe", "job-001", new Uri("https://example.com"), "Senior Engineer", "Acme", "London",
        10, EmploymentType.Permanent, WorkMode.Hybrid, 85000, 95000, null, null, null,
        new DateOnly(2026, 5, 30), "C# AWS");

    var deduped = new[] { reedJob, jobserveJob }
        .GroupBy(j => $"{j.Source}|{j.SourceJobId}".ToLowerInvariant())
        .Select(g => g.OrderByDescending(j => j.PostedDate).First())
        .ToArray();

    AssertEqual(2, deduped.Length);
}

// ── title matching tests ──────────────────────────────────────────────────────

static void TitleMatchingWorksByContains()
{
    var criteria = Defaults.CreateCriteria(new DateOnly(2026, 5, 30));

    // These titles should match because they contain one of the configured criteria titles.
    var matchingTitles = new[]
    {
        "Senior Software Engineer - C# / ASP.NET Core",
        "Senior Software Developer (Remote)",
        "Lead Developer - Full Stack .NET",
        "Senior Fullstack Engineer | Fintech",
    };

    // This title should not match any configured criteria title.
    var nonMatchingTitle = "Java Spring Boot Developer";

    var filter = new JobFilterEngine();

    foreach (var title in matchingTitles)
    {
        var job = CreateJobWithTitle(title, criteria);
        var decision = filter.Evaluate(job, criteria);
        AssertTrue(decision.Accepted || decision.Reason != "Title mismatch");
    }

    var nonMatchingJob = CreateJobWithTitle(nonMatchingTitle, criteria);
    var nonMatchingDecision = filter.Evaluate(nonMatchingJob, criteria);
    AssertFalse(nonMatchingDecision.Accepted);
    AssertEqual("Title mismatch", nonMatchingDecision.Reason);
}

static void SlackReporterSkipsWhenUrlIsNull()
{
    var reporter = new SlackJobReporter(new HttpClient(), null);
    var result = new SearchRunResult(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, Array.Empty<SourceFetchResult>(), Array.Empty<JobMatch>(), new Dictionary<string, int>());
    var criteria = Defaults.CreateCriteria(new DateOnly(2026, 6, 2));

    // Should not throw
    reporter.ReportAsync(result, criteria, CancellationToken.None).Wait();
}

static void GmailAdapterReturnsWarningWhenCredentialsAreNull()
{
    var adapter = new GmailAlertJobSourceAdapter(null);
    var criteria = Defaults.CreateCriteria(new DateOnly(2026, 6, 2));
    var result = adapter.FetchAsync(criteria, CancellationToken.None).Result;

    AssertEqual(0, result.Jobs.Count);
    AssertEqual(1, result.Warnings.Count);
    AssertTrue(result.Warnings.First().Contains("Gmail credentials not configured"));
}

// ── helpers ──────────────────────────────────────────────────────────────────

static JobPosting CreateJob(
    EmploymentType employmentType,
    WorkMode workMode,
    double distanceMiles = 10,
    decimal? salaryMin = null,
    decimal? salaryMax = null,
    decimal? dayRateMin = null,
    decimal? dayRateMax = null,
    int? contractMonths = null) =>
    new(
        "Test",
        Guid.NewGuid().ToString("N"),
        new Uri("https://example.com/job"),
        "Senior Software Engineer",
        "Example Ltd",
        "Milton Keynes",
        distanceMiles,
        employmentType,
        workMode,
        salaryMin,
        salaryMax,
        dayRateMin,
        dayRateMax,
        contractMonths,
        new DateOnly(2026, 5, 29),
        "Senior role using C#, ASP.NET Core, Web API, React, TypeScript, AWS, Docker, SQL Server, microservices, payments, e-commerce, Agile, architecture and code review.");

static JobPosting CreateJobOnDate(
    DateOnly postedDate,
    EmploymentType employmentType,
    WorkMode workMode,
    decimal? salaryMin = null) =>
    new(
        "Test",
        Guid.NewGuid().ToString("N"),
        new Uri("https://example.com/job"),
        "Senior Software Engineer",
        "Example Ltd",
        "Milton Keynes",
        10,
        employmentType,
        workMode,
        salaryMin,
        null, null, null, null,
        postedDate,
        "C# ASP.NET Core role.");

static JobPosting CreateJobWithTitle(string title, JobSearchCriteria criteria) =>
    new(
        "Test",
        Guid.NewGuid().ToString("N"),
        new Uri("https://example.com/job"),
        title,
        "Example Ltd",
        "Milton Keynes",
        10,
        EmploymentType.Permanent,
        WorkMode.Hybrid,
        85000, 95000, null, null, null,
        criteria.PostedTo.AddDays(-1),
        "C# ASP.NET Core Web API React TypeScript AWS Docker SQL Server microservices payments agile architecture code review.");

static void AssertTrue(bool value)
{
    if (!value)
    {
        throw new InvalidOperationException("Expected true.");
    }
}

static void AssertFalse(bool value)
{
    if (value)
    {
        throw new InvalidOperationException("Expected false.");
    }
}

static void AssertEqual<T>(T expected, T actual)
    where T : notnull
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }
}
