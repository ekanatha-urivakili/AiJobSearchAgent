using System.Collections.Concurrent;
using AiJobSearchAgent.Core;

namespace AiJobSearchAgent.McpServer;

public sealed class JobSearchMcpService
{
    private readonly CredentialProvider credentials;
    private readonly IHttpClientFactory httpClientFactory;
    private readonly TimeProvider timeProvider;

    private volatile ConcurrentDictionary<string, JobPosting> cache = new(StringComparer.OrdinalIgnoreCase);
    private volatile ConcurrentDictionary<string, SourceStatusDto> lastStatus = new(StringComparer.OrdinalIgnoreCase);

    private sealed record RunState(SearchRunResult Result, JobSearchCriteria Criteria, string RunId, string Report);
    private volatile RunState? latest;

    public JobSearchMcpService(CredentialProvider credentials, IHttpClientFactory httpClientFactory, TimeProvider timeProvider)
    {
        this.credentials = credentials;
        this.httpClientFactory = httpClientFactory;
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
            new CvMatchScorer()).RunAsync(criteria, Defaults.CreateCvProfile(), cancellationToken);

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
                var s when s.Equals("Indeed UK", StringComparison.OrdinalIgnoreCase) => "GMAIL_CREDENTIALS_JSON",
                _ => null
            };
            var ready = policy.Enabled
                && policy.FetchMode != FetchMode.Disabled
                && (requiredSecret is null || !string.IsNullOrWhiteSpace(credentials.GetSecret(requiredSecret)));

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
        new("Gmail Alerts", FetchMode.AlertInbox, Enabled: !string.IsNullOrWhiteSpace(credentials.GetSecret("GMAIL_CREDENTIALS_JSON")), TimeSpan.FromSeconds(0), new DateOnly(2026, 6, 3)),
        new("Indeed UK", FetchMode.AlertInbox, Enabled: !string.IsNullOrWhiteSpace(credentials.GetSecret("GMAIL_CREDENTIALS_JSON")), TimeSpan.FromSeconds(10), new DateOnly(2026, 6, 3))
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
            var gmailQuery = credentials.GetSecret("GMAIL_SEARCH_QUERY") ?? "label:job-alerts is:unread";
            yield return string.IsNullOrWhiteSpace(credJson)
                ? new PolicyBlockedSourceAdapter("Gmail Alerts", "GMAIL_CREDENTIALS_JSON is not configured.")
                : new GmailAlertJobSourceAdapter(credJson, gmailQuery);
        }

        if (selectedSources.Contains("Indeed UK") || selectedSources.Contains("Indeed"))
        {
            var credJson = credentials.GetSecret("GMAIL_CREDENTIALS_JSON");
            var indeedQuery = credentials.GetSecret("INDEED_GMAIL_SEARCH_QUERY")
                ?? "from:jobalerts-noreply@indeed.com is:unread";
            yield return string.IsNullOrWhiteSpace(credJson)
                ? new PolicyBlockedSourceAdapter("Indeed UK", "GMAIL_CREDENTIALS_JSON is not configured.")
                : new IndeedAlertJobSourceAdapter(credJson, indeedQuery);
        }
    }

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
