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
}
