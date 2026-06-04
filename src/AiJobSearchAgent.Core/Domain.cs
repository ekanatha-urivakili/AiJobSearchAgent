namespace AiJobSearchAgent.Core;

public enum EmploymentType
{
    Permanent,
    Contract
}

public enum WorkMode
{
    Remote,
    Hybrid,
    Office
}

public enum FetchMode
{
    Disabled,
    ApprovedApi,
    AlertInbox,
    PublicPage,
    McpPlugin
}

public sealed record Money(decimal Amount, string Currency)
{
    public static Money Gbp(decimal amount) => new(amount, "GBP");
}

public sealed record JobSearchCriteria(
    IReadOnlyCollection<string> Titles,
    string Postcode,
    int RadiusMiles,
    DateOnly PostedFrom,
    DateOnly PostedTo,
    IReadOnlyCollection<EmploymentType> EmploymentTypes,
    IReadOnlyCollection<WorkMode> WorkModes,
    Money MinimumPermanentSalary,
    Money MinimumContractDayRate,
    int MinimumContractMonths);

public sealed record CvProfile(
    string Name,
    string LocationPostcode,
    IReadOnlyCollection<string> CoreSkills,
    IReadOnlyCollection<string> DomainKeywords,
    IReadOnlyCollection<string> LeadershipKeywords);

public sealed record JobPosting(
    string Source,
    string SourceJobId,
    Uri Url,
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

public sealed record SourcePolicy(
    string SourceName,
    FetchMode FetchMode,
    bool Enabled,
    TimeSpan MinimumDelay,
    DateOnly LastReviewedOn);

public sealed record SourceFetchResult(
    string SourceName,
    IReadOnlyCollection<JobPosting> Jobs,
    IReadOnlyCollection<string> Warnings);

public sealed record FilterDecision(
    bool Accepted,
    string Reason);

public sealed record JobMatch(
    JobPosting Job,
    int Score,
    bool Recommended,
    IReadOnlyCollection<string> Reasons,
    IReadOnlyCollection<string> Risks);

public sealed record SearchRunResult(
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    IReadOnlyCollection<SourceFetchResult> SourceResults,
    IReadOnlyCollection<JobMatch> Matches,
    IReadOnlyDictionary<string, int> RejectedSummary);
