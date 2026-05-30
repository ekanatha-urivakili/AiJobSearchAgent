namespace AiJobSearchAgent.Core;

public interface IJobSourceAdapter
{
    string SourceName { get; }

    Task<SourceFetchResult> FetchAsync(JobSearchCriteria criteria, CancellationToken cancellationToken);
}

public sealed class SourcePolicyGuard
{
    private readonly IReadOnlyDictionary<string, SourcePolicy> policies;

    public SourcePolicyGuard(IEnumerable<SourcePolicy> policies)
    {
        this.policies = policies.ToDictionary(policy => policy.SourceName, StringComparer.OrdinalIgnoreCase);
    }

    public FilterDecision CanFetch(string sourceName)
    {
        if (!policies.TryGetValue(sourceName, out var policy))
        {
            return new(false, "Missing source policy");
        }

        if (!policy.Enabled || policy.FetchMode == FetchMode.Disabled)
        {
            return new(false, "Source disabled by policy");
        }

        return new(true, $"Allowed via {policy.FetchMode}");
    }
}

public sealed class SampleJobSourceAdapter : IJobSourceAdapter
{
    private readonly IReadOnlyCollection<JobPosting> jobs;

    public SampleJobSourceAdapter(string sourceName, IReadOnlyCollection<JobPosting> jobs)
    {
        SourceName = sourceName;
        this.jobs = jobs;
    }

    public string SourceName { get; }

    public Task<SourceFetchResult> FetchAsync(JobSearchCriteria criteria, CancellationToken cancellationToken)
    {
        var warnings = new[]
        {
            "Sample adapter uses fixture jobs. Replace with approved API, alert inbox, or permitted public-page adapter before production use."
        };

        return Task.FromResult(new SourceFetchResult(SourceName, jobs, warnings));
    }
}

public static class SampleSources
{
    public static IReadOnlyCollection<IJobSourceAdapter> Create(DateOnly today) =>
    [
        new SampleJobSourceAdapter("Reed",
        [
            new(
                "Reed",
                "reed-001",
                new Uri("https://www.reed.co.uk/jobs/example-senior-software-engineer"),
                "Senior Software Engineer - C# ASP.NET Core React",
                "Example Fintech Ltd",
                "Milton Keynes",
                4,
                EmploymentType.Permanent,
                WorkMode.Hybrid,
                80000,
                90000,
                null,
                null,
                null,
                today.AddDays(-1),
                "Build ASP.NET Core Web API services with React, TypeScript, SQL Server, AWS, Docker, CI/CD, payments and marketplace integrations.")
        ]),
        new SampleJobSourceAdapter("JobServe",
        [
            new(
                "JobServe",
                "jobserve-001",
                new Uri("https://www.jobserve.com/gb/en/example-lead-developer"),
                "Lead Developer .NET AWS",
                "Example Commerce Group",
                "London",
                47,
                EmploymentType.Contract,
                WorkMode.Hybrid,
                null,
                null,
                500,
                575,
                6,
                today.AddDays(-2),
                "Lead a senior engineering team delivering C#, ASP.NET Core, REST APIs, AWS, PostgreSQL, React, Agile delivery, code review and e-commerce integrations."),
            new(
                "JobServe",
                "jobserve-002",
                new Uri("https://www.jobserve.com/gb/en/example-low-rate"),
                "Senior Software Developer",
                "Example Agency",
                "Oxford",
                42,
                EmploymentType.Contract,
                WorkMode.Office,
                null,
                null,
                350,
                375,
                6,
                today.AddDays(-1),
                "C# developer role with SQL and Web API.")
        ])
    ];
}
