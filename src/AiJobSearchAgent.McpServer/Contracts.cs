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
