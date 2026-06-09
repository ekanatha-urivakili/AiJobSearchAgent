using System.Collections.Concurrent;
using AiJobSearchAgent.Core;

namespace AiJobSearchAgent.McpServer;

public sealed class JobSearchMcpService
{
    private readonly CredentialProvider credentials;
    private readonly IHttpClientFactory httpClientFactory;
    private readonly JobRunRepository jobRunRepository;
    private readonly TimeProvider timeProvider;

    private volatile ConcurrentDictionary<string, JobPosting> cache = new(StringComparer.OrdinalIgnoreCase);
    private volatile ConcurrentDictionary<string, SourceStatusDto> lastStatus = new(StringComparer.OrdinalIgnoreCase);
    private readonly IndeedDirectJobSourceAdapter indeedDirect = new();
    private readonly AiJobSearchAgent.Core.PluginJobSourceAdapter diceAdapter = new("Dice");
    private readonly AiJobSearchAgent.Core.PluginJobSourceAdapter zipRecruiterAdapter = new("ZipRecruiter");

    private sealed record RunState(SearchRunResult Result, JobSearchCriteria Criteria, string RunId, string Report);
    private volatile RunState? latest;

    public JobSearchMcpService(
        CredentialProvider credentials,
        IHttpClientFactory httpClientFactory,
        JobRunRepository jobRunRepository,
        TimeProvider timeProvider)
    {
        this.credentials = credentials;
        this.httpClientFactory = httpClientFactory;
        this.jobRunRepository = jobRunRepository;
        this.timeProvider = timeProvider;
    }

    public async Task<SearchJobsResponse> SearchJobsAsync(SearchJobsRequest request, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime);
        var criteria = CreateCriteria(request, today);
        var policies = CreatePolicies();
        var selectedSources = request.Sources?.Count > 0
            ? request.Sources.ToHashSet(StringComparer.OrdinalIgnoreCase)
            : policies.Select(policy => policy.SourceName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sources = CreateAdapters(criteria, selectedSources).ToArray();
        var result = await new JobSearchOrchestrator(
            sources,
            new SourcePolicyGuard(policies),
            new JobFilterEngine(),
            new CvMatchScorer(today)).RunAsync(criteria, Defaults.CreateCvProfile(), cancellationToken);

        var runId = Guid.CreateVersion7().ToString();
        var report = new MarkdownReportGenerator().Generate(result, criteria);

        var newCache = new ConcurrentDictionary<string, JobPosting>(StringComparer.OrdinalIgnoreCase);
        foreach (var job in result.SourceResults.SelectMany(sr => sr.Jobs))
        {
            newCache[CacheKey(job.Source, job.SourceJobId)] = job;
        }

        var sourceStatus = BuildSourceStatus(policies, result).ToArray();
        var newStatus = new ConcurrentDictionary<string, SourceStatusDto>(StringComparer.OrdinalIgnoreCase);
        foreach (var status in sourceStatus)
        {
            newStatus[status.Source] = status;
        }

        latest = new RunState(result, criteria, runId, report);
        cache = newCache;
        lastStatus = newStatus;
        await jobRunRepository.SaveRunAsync(Guid.Parse(runId), result, cancellationToken);

        return new(
            runId,
            $"jobsearch://reports/{criteria.PostedTo:yyyy-MM-dd}",
            sourceStatus,
            result.Matches.Select(ToMatchDto).ToArray(),
            result.RejectedSummary);
    }

    public async Task<GetJobResponse> GetJobAsync(string source, string sourceJobId, CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(CacheKey(source, sourceJobId), out var cachedJob))
        {
            return new(true, ToJobDto(cachedJob), null);
        }

        if (!source.Equals("Reed", StringComparison.OrdinalIgnoreCase))
        {
            return new(false, null, "Job not found in local cache.");
        }

        var apiKey = credentials.GetSecret("REED_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new(false, null, "REED_API_KEY is not configured.");
        }

        var adapter = new ReedApiJobSourceAdapter(httpClientFactory.CreateClient("Reed"), apiKey);
        var job = await adapter.FetchJobAsync(sourceJobId, cancellationToken);
        if (job is null)
        {
            return new(false, null, "Reed job was not returned by the approved API or lacked required fields.");
        }

        cache[CacheKey(job.Source, job.SourceJobId)] = job;
        return new(true, ToJobDto(job), null);
    }

    public async Task<GenerateReportResponse> GenerateReportAsync(CancellationToken cancellationToken)
    {
        var state = latest;
        if (state is null)
        {
            await SearchJobsAsync(SearchJobsRequest.Default, cancellationToken);
            state = latest ?? throw new InvalidOperationException("Search run did not produce state.");
        }

        return new(state.RunId, $"jobsearch://reports/{state.Criteria.PostedTo:yyyy-MM-dd}", state.Report);
    }

    public SourceHealthResponse SourceHealth()
    {
        var sources = CreatePolicies().Select(policy =>
        {
            var requiredSecret = policy.SourceName switch
            {
                var s when s.Equals("Reed", StringComparison.OrdinalIgnoreCase) => "REED_API_KEY",
                var s when s.Equals("Gmail Alerts", StringComparison.OrdinalIgnoreCase) => "GMAIL_CREDENTIALS_JSON and GMAIL_USER_EMAIL",
                var s when s.Equals("Indeed Direct", StringComparison.OrdinalIgnoreCase) => "Call jobs.ingest_indeed before jobs.search",
                var s when s.Equals("Dice", StringComparison.OrdinalIgnoreCase) => "Call jobs.ingest_dice before jobs.search",
                var s when s.Equals("ZipRecruiter", StringComparison.OrdinalIgnoreCase) => "Call jobs.ingest_ziprecruiter before jobs.search",
                _ => null
            };
            var ready = policy.Enabled
                && policy.FetchMode != FetchMode.Disabled
                && SourceHasRequiredConfiguration(policy.SourceName);

            lastStatus.TryGetValue(policy.SourceName, out var status);
            return new SourceHealthDto(
                policy.SourceName,
                policy.FetchMode,
                policy.Enabled,
                ready,
                requiredSecret,
                status?.Status,
                policy.LastReviewedOn);
        }).ToArray();

        return new(sources);
    }

    public IngestJobsResponse IngestDiceJobs(IngestDiceJobsRequest request)
    {
        if (request.ClearFirst) diceAdapter.Clear();

        var today = DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime);
        var jobs = request.Jobs.Select(j =>
        {
            var (salMin, salMax) = ParseDiceSalary(j.SalaryRaw);
            return new AiJobSearchAgent.Core.JobPosting(
                "Dice", j.JobId,
                Uri.TryCreate(j.Url, UriKind.Absolute, out var u) ? u : new Uri($"https://www.dice.com/job-detail/{j.JobId}"),
                j.Title, j.Company, j.Location, 0,
                j.EmploymentType, j.WorkMode,
                j.EmploymentType == AiJobSearchAgent.Core.EmploymentType.Permanent ? salMin : null,
                j.EmploymentType == AiJobSearchAgent.Core.EmploymentType.Permanent ? salMax : null,
                j.EmploymentType == AiJobSearchAgent.Core.EmploymentType.Contract ? salMin : null,
                j.EmploymentType == AiJobSearchAgent.Core.EmploymentType.Contract ? salMax : null,
                null, today, j.Description ?? string.Empty);
        });
        diceAdapter.Ingest(jobs);
        return new(request.Jobs.Count, diceAdapter.Count, $"Ingested {request.Jobs.Count} Dice jobs ({diceAdapter.Count} total buffered).");
    }

    public IngestJobsResponse IngestZipRecruiterJobs(IngestZipRecruiterJobsRequest request)
    {
        if (request.ClearFirst) zipRecruiterAdapter.Clear();

        var today = DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime);
        var jobs = request.Jobs.Select(j => new AiJobSearchAgent.Core.JobPosting(
            "ZipRecruiter", j.JobId,
            Uri.TryCreate(j.Url, UriKind.Absolute, out var u) ? u : new Uri($"https://www.ziprecruiter.com"),
            j.Title, j.Company, j.Location, 0,
            j.EmploymentType, j.WorkMode,
            j.EmploymentType == AiJobSearchAgent.Core.EmploymentType.Permanent ? j.SalaryMinUsd : null,
            j.EmploymentType == AiJobSearchAgent.Core.EmploymentType.Permanent ? j.SalaryMaxUsd : null,
            j.EmploymentType == AiJobSearchAgent.Core.EmploymentType.Contract ? j.SalaryMinUsd : null,
            j.EmploymentType == AiJobSearchAgent.Core.EmploymentType.Contract ? j.SalaryMaxUsd : null,
            null, today, j.Description ?? string.Empty));
        zipRecruiterAdapter.Ingest(jobs);
        return new(request.Jobs.Count, zipRecruiterAdapter.Count, $"Ingested {request.Jobs.Count} ZipRecruiter jobs ({zipRecruiterAdapter.Count} total buffered).");
    }

    /// <summary>Parses Dice salary strings like "USD 170,000.00 - 270,000.00 per year" into (min, max).</summary>
    private static (decimal? min, decimal? max) ParseDiceSalary(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return (null, null);
        var numbers = System.Text.RegularExpressions.Regex.Matches(raw, @"[\d,]+\.?\d*")
            .Select(m => decimal.TryParse(m.Value.Replace(",", ""), out var v) ? v : (decimal?)null)
            .Where(v => v.HasValue && v.Value > 1000)
            .Select(v => v!.Value)
            .ToArray();
        return numbers.Length switch
        {
            0 => (null, null),
            1 => (numbers[0], null),
            _ => (numbers[0], numbers[1])
        };
    }

    /// <summary>Returns the cached result from the last run, or runs a fresh default search if none exists.</summary>
    public Task<SearchJobsResponse> GetLatestOrSearchAsync(SearchJobsRequest request, CancellationToken cancellationToken)
    {
        if (latest is not null)
        {
            var cached = latest;
            var sourceStatus = BuildSourceStatus(CreatePolicies(), cached.Result).ToArray();
            return Task.FromResult(new SearchJobsResponse(
                cached.RunId,
                $"jobsearch://reports/{cached.Criteria.PostedTo:yyyy-MM-dd}",
                sourceStatus,
                cached.Result.Matches.Select(ToMatchDto).ToArray(),
                cached.Result.RejectedSummary));
        }
        return SearchJobsAsync(request, cancellationToken);
    }

    public IngestIndeedJobsResponse IngestIndeedJobs(IngestIndeedJobsRequest request)
    {
        if (request.ClearFirst)
            indeedDirect.Clear();

        var today = DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime);
        var jobs = request.Jobs.Select(j => new JobPosting(
            "Indeed Direct",
            j.JobId,
            Uri.TryCreate(j.Url, UriKind.Absolute, out var uri) ? uri : new Uri($"https://uk.indeed.com/viewjob?jk={j.JobId}"),
            j.Title,
            j.Company,
            j.Location,
            0,
            j.EmploymentType,
            j.WorkMode,
            j.EmploymentType == EmploymentType.Permanent ? j.SalaryMin : null,
            j.EmploymentType == EmploymentType.Permanent ? j.SalaryMax : null,
            j.EmploymentType == EmploymentType.Contract ? j.DayRateMin : null,
            j.EmploymentType == EmploymentType.Contract ? j.DayRateMax : null,
            j.ContractMonths,
            today,
            j.Description ?? string.Empty));

        indeedDirect.Ingest(jobs);
        return new(request.Jobs.Count, indeedDirect.Count, $"Ingested {request.Jobs.Count} Indeed Direct jobs ({indeedDirect.Count} total buffered).");
    }

    private static JobSearchCriteria CreateCriteria(SearchJobsRequest request, DateOnly today)
    {
        var defaults = Defaults.CreateCriteria(today);
        var postedWithinDays = request.PostedWithinDays ?? 7;
        return defaults with
        {
            Titles = request.Keywords?.Count > 0 ? request.Keywords : defaults.Titles,
            Postcode = string.IsNullOrWhiteSpace(request.Postcode) ? defaults.Postcode : request.Postcode,
            RadiusMiles = request.RadiusMiles ?? defaults.RadiusMiles,
            PostedFrom = today.AddDays(-postedWithinDays),
            PostedTo = today,
            EmploymentTypes = request.EmploymentTypes?.Count > 0 ? request.EmploymentTypes : defaults.EmploymentTypes,
            WorkModes = request.WorkModes?.Count > 0 ? request.WorkModes : defaults.WorkModes,
            MinimumPermanentSalary = Money.Gbp(request.MinimumPermanentSalaryGbp ?? defaults.MinimumPermanentSalary.Amount),
            MinimumContractDayRate = Money.Gbp(request.MinimumContractDayRateGbp ?? defaults.MinimumContractDayRate.Amount),
            MinimumContractMonths = request.MinimumContractMonths ?? defaults.MinimumContractMonths
        };
    }

    private IReadOnlyCollection<SourcePolicy> CreatePolicies() =>
    [
        new("Reed", FetchMode.ApprovedApi, Enabled: !string.IsNullOrWhiteSpace(credentials.GetSecret("REED_API_KEY")), TimeSpan.FromSeconds(3), new DateOnly(2026, 6, 1)),
        new("Gmail Alerts", FetchMode.AlertInbox, Enabled: HasGmailConfiguration(), TimeSpan.FromSeconds(0), new DateOnly(2026, 6, 3)),
        new("Indeed Direct", FetchMode.McpPlugin, Enabled: true, TimeSpan.FromSeconds(0), new DateOnly(2026, 6, 4)),
        new("Dice", FetchMode.McpPlugin, Enabled: true, TimeSpan.FromSeconds(0), new DateOnly(2026, 6, 4)),
        new("ZipRecruiter", FetchMode.McpPlugin, Enabled: true, TimeSpan.FromSeconds(0), new DateOnly(2026, 6, 4))
    ];

    private IEnumerable<IJobSourceAdapter> CreateAdapters(JobSearchCriteria criteria, IReadOnlySet<string> selectedSources)
    {
        if (selectedSources.Contains("Reed"))
        {
            var apiKey = credentials.GetSecret("REED_API_KEY");
            yield return string.IsNullOrWhiteSpace(apiKey)
                ? new PolicyBlockedSourceAdapter("Reed", "REED_API_KEY is not configured.")
                : new ReedApiJobSourceAdapter(httpClientFactory.CreateClient("Reed"), apiKey);
        }

        if (selectedSources.Contains("Gmail Alerts"))
        {
            var credJson = credentials.GetSecret("GMAIL_CREDENTIALS_JSON");
            var userEmail = credentials.GetSecret("GMAIL_USER_EMAIL");
            var gmailQuery = credentials.GetSecret("GMAIL_SEARCH_QUERY") ?? "label:job-alerts is:unread";
            yield return !HasGmailConfiguration()
                ? new PolicyBlockedSourceAdapter("Gmail Alerts", "GMAIL_CREDENTIALS_JSON and GMAIL_USER_EMAIL are required.")
                : new GmailAlertJobSourceAdapter(credJson, gmailQuery, userEmail);
        }

        if (selectedSources.Contains("Indeed Direct"))
            yield return indeedDirect;

        if (selectedSources.Contains("Dice"))
            yield return diceAdapter;

        if (selectedSources.Contains("ZipRecruiter"))
            yield return zipRecruiterAdapter;

    }

    private bool SourceHasRequiredConfiguration(string sourceName)
    {
        if (sourceName.Equals("Reed", StringComparison.OrdinalIgnoreCase))
        {
            return !string.IsNullOrWhiteSpace(credentials.GetSecret("REED_API_KEY"));
        }

        if (sourceName.Equals("Gmail Alerts", StringComparison.OrdinalIgnoreCase))
        {
            return HasGmailConfiguration();
        }

        if (sourceName.Equals("Indeed Direct", StringComparison.OrdinalIgnoreCase))
            return indeedDirect.Count > 0;

        if (sourceName.Equals("Dice", StringComparison.OrdinalIgnoreCase))
            return diceAdapter.Count > 0;

        if (sourceName.Equals("ZipRecruiter", StringComparison.OrdinalIgnoreCase))
            return zipRecruiterAdapter.Count > 0;

        return true;
    }

    private bool HasGmailConfiguration() =>
        !string.IsNullOrWhiteSpace(credentials.GetSecret("GMAIL_CREDENTIALS_JSON"))
        && !string.IsNullOrWhiteSpace(credentials.GetSecret("GMAIL_USER_EMAIL"));

    private static IEnumerable<SourceStatusDto> BuildSourceStatus(
        IReadOnlyCollection<SourcePolicy> policies,
        SearchRunResult result)
    {
        var resultBySource = result.SourceResults.ToDictionary(sr => sr.SourceName, StringComparer.OrdinalIgnoreCase);
        foreach (var policy in policies)
        {
            if (resultBySource.TryGetValue(policy.SourceName, out var sourceResult))
            {
                yield return new(policy.SourceName, "Succeeded", sourceResult.Jobs.Count, policy.FetchMode, sourceResult.Warnings);
                continue;
            }

            var blocked = result.RejectedSummary.Keys.FirstOrDefault(reason => reason.StartsWith($"{policy.SourceName}:", StringComparison.OrdinalIgnoreCase));
            yield return new(
                policy.SourceName,
                blocked is null ? "Skipped" : "PolicyBlocked",
                0,
                policy.FetchMode,
                blocked is null ? [] : [blocked]);
        }
    }

    private static JobMatchDto ToMatchDto(JobMatch match) =>
        new(
            match.Job.Source,
            match.Job.SourceJobId,
            match.Job.Title,
            match.Job.Company,
            match.Job.Location,
            match.Job.DistanceMiles,
            match.Job.EmploymentType,
            match.Job.WorkMode,
            match.Job.SalaryMin,
            match.Job.SalaryMax,
            match.Job.DayRateMin,
            match.Job.DayRateMax,
            match.Job.ContractMonths,
            match.Job.PostedDate,
            match.Score,
            match.Recommended,
            match.Reasons,
            match.Risks,
            match.Job.Url.ToString());

    private static JobDto ToJobDto(JobPosting job) =>
        new(
            job.Source,
            job.SourceJobId,
            job.Url.ToString(),
            job.Title,
            job.Company,
            job.Location,
            job.DistanceMiles,
            job.EmploymentType,
            job.WorkMode,
            job.SalaryMin,
            job.SalaryMax,
            job.DayRateMin,
            job.DayRateMax,
            job.ContractMonths,
            job.PostedDate,
            job.Description);

    private static string CacheKey(string source, string sourceJobId) => $"{source}:{sourceJobId}";
}
