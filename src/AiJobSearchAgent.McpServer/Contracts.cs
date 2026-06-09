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

public sealed record JobApplicationDto(
    string Source,
    string SourceJobId,
    string Status,
    string Notes,
    DateTimeOffset UpdatedAt);

public sealed record SaveJobApplicationRequest(string Status, string? Notes);

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

/// <summary>
/// One job as returned by the Dice MCP plugin.
/// salary field format: "USD 170,000.00 - 270,000.00 per year" or "USD 130,000.00 per year".
/// </summary>
public sealed record DiceJobInput(
    [property: System.ComponentModel.Description("Dice job GUID, e.g. 60da0443-3c8e-498f-afad-ed11e9243926.")]
    string JobId,
    [property: System.ComponentModel.Description("Job title.")]
    string Title,
    [property: System.ComponentModel.Description("Company name.")]
    string Company,
    [property: System.ComponentModel.Description("Location display name, e.g. 'London' or 'Remote'.")]
    string Location,
    [property: System.ComponentModel.Description("Full detailsPageUrl from Dice.")]
    string Url,
    [property: System.ComponentModel.Description("Permanent or Contract.")]
    EmploymentType EmploymentType,
    [property: System.ComponentModel.Description("Remote, Hybrid, or Office.")]
    WorkMode WorkMode,
    [property: System.ComponentModel.Description("Raw salary string from Dice, e.g. 'USD 170,000.00 - 270,000.00 per year'. Null if not provided.")]
    string? SalaryRaw,
    [property: System.ComponentModel.Description("Job summary / description text.")]
    string? Description);

public sealed record IngestDiceJobsRequest(
    IReadOnlyCollection<DiceJobInput> Jobs,
    bool ClearFirst = true);

/// <summary>
/// One job as returned by the ZipRecruiter MCP plugin.
/// Salary values are in USD annually. ZipRecruiter is US/Canada only.
/// </summary>
public sealed record ZipRecruiterJobInput(
    [property: System.ComponentModel.Description("Stable job ID extracted from jid= param in job_redirect_url, or a slug derived from title+company.")]
    string JobId,
    [property: System.ComponentModel.Description("Job title.")]
    string Title,
    [property: System.ComponentModel.Description("Company name.")]
    string Company,
    [property: System.ComponentModel.Description("Location string, e.g. 'New York, NY' or 'Remote'.")]
    string Location,
    [property: System.ComponentModel.Description("job_redirect_url from ZipRecruiter.")]
    string Url,
    [property: System.ComponentModel.Description("Permanent or Contract.")]
    EmploymentType EmploymentType,
    [property: System.ComponentModel.Description("Remote, Hybrid, or Office.")]
    WorkMode WorkMode,
    [property: System.ComponentModel.Description("Minimum annual salary in USD (from salary.min_annual). Null if not published.")]
    decimal? SalaryMinUsd,
    [property: System.ComponentModel.Description("Maximum annual salary in USD (from salary.max_annual). Null if not published.")]
    decimal? SalaryMaxUsd,
    [property: System.ComponentModel.Description("Job description or benefits string.")]
    string? Description);

public sealed record IngestZipRecruiterJobsRequest(
    IReadOnlyCollection<ZipRecruiterJobInput> Jobs,
    bool ClearFirst = true);

public sealed record IngestJobsResponse(
    int Ingested,
    int TotalBuffered,
    string Message);
