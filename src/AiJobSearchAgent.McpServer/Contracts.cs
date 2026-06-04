using AiJobSearchAgent.Core;

namespace AiJobSearchAgent.McpServer;

public sealed record SearchJobsRequest(
    IReadOnlyCollection<string>? Keywords,
    string? Postcode,
    int? RadiusMiles,
    int? PostedWithinDays,
    IReadOnlyCollection<EmploymentType>? EmploymentTypes,
    IReadOnlyCollection<WorkMode>? WorkModes,
    decimal? MinimumPermanentSalaryGbp,
    decimal? MinimumContractDayRateGbp,
    int? MinimumContractMonths,
    IReadOnlyCollection<string>? Sources)
{
    public static SearchJobsRequest Default { get; } = new(null, null, null, null, null, null, null, null, null, null);
}

public sealed record SearchJobsResponse(
    string RunId,
    string ReportResource,
    IReadOnlyCollection<SourceStatusDto> SourceStatus,
    IReadOnlyCollection<JobMatchDto> Matches,
    IReadOnlyDictionary<string, int> RejectedSummary);

public sealed record GetJobResponse(bool Found, JobDto? Job, string? Message);

public sealed record GenerateReportResponse(string RunId, string ReportResource, string Markdown);

public sealed record SourceHealthResponse(IReadOnlyCollection<SourceHealthDto> Sources);

public sealed record SourceHealthDto(
    string Source,
    FetchMode Mode,
    bool Enabled,
    bool Ready,
    string? RequiredSecret,
    string? LastStatus,
    DateOnly LastReviewedOn);

public sealed record SourceStatusDto(
    string Source,
    string Status,
    int JobsFetched,
    FetchMode Mode,
    IReadOnlyCollection<string> Warnings);

public sealed record JobMatchDto(
    string Source,
    string SourceJobId,
    string Title,
    string Company,
    string Location,
    double DistanceMiles,
    EmploymentType EmploymentType,
    WorkMode WorkMode,
    decimal? SalaryMin,
    decimal? SalaryMax,
    decimal? DayRateMin,
    decimal? DayRateMax,
    int? ContractMonths,
    DateOnly PostedDate,
    int Score,
    bool Recommended,
    IReadOnlyCollection<string> Reasons,
    IReadOnlyCollection<string> Risks,
    string Url);

public sealed record JobDto(
    string Source,
    string SourceJobId,
    string Url,
    string Title,
    string Company,
    string Location,
    double DistanceMiles,
    EmploymentType EmploymentType,
    WorkMode WorkMode,
    decimal? SalaryMin,
    decimal? SalaryMax,
    decimal? DayRateMin,
    decimal? DayRateMax,
    int? ContractMonths,
    DateOnly PostedDate,
    string Description);

/// <summary>
/// One job as returned by the Indeed MCP plugin (search_jobs / get_job_details).
/// Claude maps the Indeed plugin output to this shape before calling jobs.ingest_indeed.
/// </summary>
public sealed record IndeedJobInput(
    [property: System.ComponentModel.Description("Indeed job key (jk parameter), e.g. abc1234567890000.")]
    string JobId,
    [property: System.ComponentModel.Description("Job title.")]
    string Title,
    [property: System.ComponentModel.Description("Company name.")]
    string Company,
    [property: System.ComponentModel.Description("Location string, e.g. 'London' or 'Remote'.")]
    string Location,
    [property: System.ComponentModel.Description("Full apply URL from Indeed.")]
    string Url,
    [property: System.ComponentModel.Description("Permanent or Contract.")]
    EmploymentType EmploymentType,
    [property: System.ComponentModel.Description("Remote, Hybrid, or Office.")]
    WorkMode WorkMode,
    [property: System.ComponentModel.Description("Minimum annual salary in GBP (permanent roles).")]
    decimal? SalaryMin,
    [property: System.ComponentModel.Description("Maximum annual salary in GBP (permanent roles).")]
    decimal? SalaryMax,
    [property: System.ComponentModel.Description("Minimum day rate in GBP (contract roles).")]
    decimal? DayRateMin,
    [property: System.ComponentModel.Description("Maximum day rate in GBP (contract roles).")]
    decimal? DayRateMax,
    [property: System.ComponentModel.Description("Contract duration in months (contract roles).")]
    int? ContractMonths,
    [property: System.ComponentModel.Description("Job description text.")]
    string? Description);

public sealed record IngestIndeedJobsRequest(
    [property: System.ComponentModel.Description("Jobs to inject into the search pipeline.")]
    IReadOnlyCollection<IndeedJobInput> Jobs,
    [property: System.ComponentModel.Description("When true, clears previously ingested jobs before adding the new batch.")]
    bool ClearFirst = true);

public sealed record IngestIndeedJobsResponse(
    int Ingested,
    int TotalBuffered,
    string Message);
