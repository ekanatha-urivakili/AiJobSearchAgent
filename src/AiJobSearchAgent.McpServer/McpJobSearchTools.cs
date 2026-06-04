using System.ComponentModel;
using AiJobSearchAgent.Core;
using ModelContextProtocol.Server;

namespace AiJobSearchAgent.McpServer;

[McpServerToolType]
public sealed class McpJobSearchTools
{
    private readonly JobSearchMcpService service;

    public McpJobSearchTools(JobSearchMcpService service)
    {
        this.service = service;
    }

    [McpServerTool(Name = "jobs.search", Destructive = false, ReadOnly = true)]
    [Description("Search configured job sources through approved APIs or imported alert data, then filter and score matches.")]
    public Task<SearchJobsResponse> SearchJobsAsync(
        CancellationToken cancellationToken,
        [Description("Target job titles or keywords. Defaults to the local profile titles when omitted.")] IReadOnlyCollection<string>? keywords = null,
        [Description("UK postcode used as the search origin. Defaults to the local profile postcode.")] string? postcode = null,
        [Description("Search radius in miles. Defaults to the local profile radius.")] int? radiusMiles = null,
        [Description("Only include jobs posted within this many days. Defaults to 7.")] int? postedWithinDays = null,
        [Description("Employment types to include. Defaults to Permanent and Contract.")] IReadOnlyCollection<EmploymentType>? employmentTypes = null,
        [Description("Work modes to include. Defaults to Remote, Hybrid, and Office.")] IReadOnlyCollection<WorkMode>? workModes = null,
        [Description("Minimum permanent salary in GBP. Defaults to the local profile threshold.")] decimal? minimumPermanentSalaryGbp = null,
        [Description("Minimum contract day rate in GBP. Defaults to the local profile threshold.")] decimal? minimumContractDayRateGbp = null,
        [Description("Minimum contract duration in months. Defaults to the local profile threshold.")] int? minimumContractMonths = null,
        [Description("Source names to search. Defaults to all configured sources.")] IReadOnlyCollection<string>? sources = null) =>
        service.SearchJobsAsync(
            new(
                keywords,
                postcode,
                radiusMiles,
                postedWithinDays,
                employmentTypes,
                workModes,
                minimumPermanentSalaryGbp,
                minimumContractDayRateGbp,
                minimumContractMonths,
                sources),
            cancellationToken);

    [McpServerTool(Name = "jobs.get", Destructive = false, ReadOnly = true)]
    [Description("Return one normalized job from the local cache, or Reed API when the source is Reed and REED_API_KEY is configured.")]
    public Task<GetJobResponse> GetJobAsync(
        [Description("Source name, for example Reed.")] string source,
        [Description("Source-specific job identifier.")] string sourceJobId,
        CancellationToken cancellationToken) =>
        service.GetJobAsync(source, sourceJobId, cancellationToken);

    [McpServerTool(Name = "jobs.generate_report", Destructive = false, ReadOnly = true)]
    [Description("Generate a Markdown report for the latest search run or a new default search.")]
    public Task<GenerateReportResponse> GenerateReportAsync(CancellationToken cancellationToken) =>
        service.GenerateReportAsync(cancellationToken);

    [McpServerTool(Name = "sources.health", Destructive = false, ReadOnly = true)]
    [Description("Return source policy, configured mode, secret readiness, and last local fetch status.")]
    public SourceHealthResponse SourceHealth() => service.SourceHealth();

    [McpServerTool(Name = "jobs.ingest_indeed", Destructive = false, ReadOnly = false)]
    [Description("""
        Ingest jobs fetched from the Indeed MCP plugin into the local search pipeline.

        Workflow:
          1. Call the Indeed MCP plugin's search_jobs tool with your desired keywords and location.
          2. Map each result to IndeedJobInput — set EmploymentType (Permanent/Contract) and WorkMode
             (Remote/Hybrid/Office) based on the job description; parse salary/day-rate from the
             salary string if provided.
          3. Call this tool with the mapped jobs (ClearFirst=true to replace any previous batch).
          4. Call jobs.search with sources=["Indeed Direct"] (or omit sources to include all).

        The injected jobs flow through the standard filter/score pipeline exactly like Reed results.
        """)]
    public IngestIndeedJobsResponse IngestIndeedJobs(
        [Description("Jobs fetched from the Indeed plugin, mapped to the IndeedJobInput shape.")]
        IReadOnlyCollection<IndeedJobInput> jobs,
        [Description("When true (default), clears previously ingested jobs before adding the new batch.")]
        bool clearFirst = true) =>
        service.IngestIndeedJobs(new(jobs, clearFirst));

    [McpServerTool(Name = "jobs.ingest_dice", Destructive = false, ReadOnly = false)]
    [Description("""
        Ingest jobs fetched from the Dice MCP plugin into the local search pipeline.

        Workflow:
          1. Call the Dice MCP plugin's search_jobs tool with your keywords and location.
          2. For each result map: guid → JobId, detailsPageUrl → Url, salary string → SalaryRaw,
             workplaceTypes → WorkMode (Remote/Hybrid/Office), employmentType → EmploymentType.
          3. Call this tool (ClearFirst=true to replace previous batch).
          4. Call jobs.search with sources=["Dice"] or omit to include all sources.

        Salary is parsed from Dice's "USD 170,000.00 - 270,000.00 per year" string automatically.
        Note: Dice salaries are in USD. The filter uses the configured GBP threshold; adjust
        minimumPermanentSalaryGbp in jobs.search if needed.
        """)]
    public IngestJobsResponse IngestDiceJobs(
        [Description("Jobs fetched from Dice plugin, mapped to DiceJobInput.")]
        IReadOnlyCollection<DiceJobInput> jobs,
        [Description("When true (default), clears previously ingested Dice jobs first.")]
        bool clearFirst = true) =>
        service.IngestDiceJobs(new(jobs, clearFirst));

    [McpServerTool(Name = "jobs.ingest_ziprecruiter", Destructive = false, ReadOnly = false)]
    [Description("""
        Ingest jobs fetched from the ZipRecruiter MCP plugin into the local search pipeline.

        Workflow:
          1. Call the ZipRecruiter MCP plugin's search_jobs tool with your query and location.
          2. For each result: extract jid= from job_redirect_url → JobId,
             salary.min_annual / salary.max_annual → SalaryMinUsd / SalaryMaxUsd,
             is_remote → WorkMode (Remote=0 / Hybrid=1 / Office=2).
          3. Call this tool (ClearFirst=true to replace previous batch).
          4. Call jobs.search with sources=["ZipRecruiter"] or omit to include all sources.

        Note: ZipRecruiter is US/Canada only. Salaries are in USD.
        """)]
    public IngestJobsResponse IngestZipRecruiterJobs(
        [Description("Jobs fetched from ZipRecruiter plugin, mapped to ZipRecruiterJobInput.")]
        IReadOnlyCollection<ZipRecruiterJobInput> jobs,
        [Description("When true (default), clears previously ingested ZipRecruiter jobs first.")]
        bool clearFirst = true) =>
        service.IngestZipRecruiterJobs(new(jobs, clearFirst));
}