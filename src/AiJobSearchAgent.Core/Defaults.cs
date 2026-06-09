namespace AiJobSearchAgent.Core;

public static class Defaults
{
    public static JobSearchCriteria CreateCriteria(DateOnly today)
    {
        var postcode = Environment.GetEnvironmentVariable("JOB_SEARCH_POSTCODE") ?? "MK4 4QG";
        var radius = int.TryParse(Environment.GetEnvironmentVariable("JOB_SEARCH_RADIUS_MILES"), out var r) ? r : 50;
        var postedWithin = int.TryParse(Environment.GetEnvironmentVariable("JOB_SEARCH_POSTED_WITHIN_DAYS"), out var p) ? p : 7;
        var minSalary = decimal.TryParse(Environment.GetEnvironmentVariable("JOB_SEARCH_MIN_PERMANENT_SALARY_GBP"), out var s) ? s : 75000m;
        var minRate = decimal.TryParse(Environment.GetEnvironmentVariable("JOB_SEARCH_MIN_CONTRACT_DAY_RATE_GBP"), out var d) ? d : 400m;
        var minMonths = int.TryParse(Environment.GetEnvironmentVariable("JOB_SEARCH_MIN_CONTRACT_MONTHS"), out var m) ? m : 6;
        var excludedKeywords = ReadCsv(
            Environment.GetEnvironmentVariable("JOB_SEARCH_EXCLUDED_KEYWORDS"),
            ["graduate", "junior", "java only", "onsite 5 days", "5 days onsite", "sc clearance"]);

        var defaultTitles = new[]
        {
            "Senior Software Engineer",
            "Senior Fullstack Engineer",
            "Senior Software Developer",
            "Lead Developer",
            "Lead Software Engineer",
            "Principal Engineer",
            "Principal Developer"
        };
        var designationEnv = Environment.GetEnvironmentVariable("JOB_SEARCH_DESIRED_DESIGNATION");
        var titles = ReadCsv(designationEnv, defaultTitles);

        return new(
            Titles: titles,
            Postcode: postcode,
            RadiusMiles: radius,
            PostedFrom: today.AddDays(-postedWithin),
            PostedTo: today,
            EmploymentTypes: [EmploymentType.Permanent, EmploymentType.Contract],
            WorkModes: [WorkMode.Remote, WorkMode.Hybrid, WorkMode.Office],
            MinimumPermanentSalary: Money.Gbp(minSalary),
            MinimumContractDayRate: Money.Gbp(minRate),
            MinimumContractMonths: minMonths,
            ExcludedKeywords: excludedKeywords);
    }

    public static CvProfile CreateCvProfile()
    {
        var defaultSkills = new[]
        {
            "c#", "asp.net core", "web api", "react", "typescript", "javascript",
            "php", "aws", "docker", "sql server", "postgresql", "mysql", "mongodb",
            "microservices", "cqrs", "rest"
        };
        var skillsEnv = Environment.GetEnvironmentVariable("JOB_SEARCH_SKILLS");
        var coreSkills = !string.IsNullOrWhiteSpace(skillsEnv)
            ? skillsEnv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                       .Select(s => s.ToLowerInvariant())
                       .ToArray()
            : defaultSkills;

        return new(
            Name: "Ekanatha Reddy Urivakili",
            LocationPostcode: Environment.GetEnvironmentVariable("JOB_SEARCH_POSTCODE") ?? "MK4 4QG",
            CoreSkills: coreSkills,
            DomainKeywords:
            [
                "fintech",
                "payments",
                "e-commerce",
                "marketplace",
                "magento",
                "bi",
                "analytics",
                "reporting",
                "retail"
            ],
            LeadershipKeywords:
            [
                "lead",
                "senior",
                "mentor",
                "architecture",
                "stakeholder",
                "agile",
                "scrum",
                "code review"
            ]);
    }

    public static IReadOnlyCollection<SourcePolicy> CreateSourcePolicies() =>
    [
        new("Reed", FetchMode.ApprovedApi, Enabled: true, TimeSpan.FromSeconds(3), new DateOnly(2026, 6, 3)),
        new("Gmail Alerts", FetchMode.AlertInbox, Enabled: true, TimeSpan.FromSeconds(0), new DateOnly(2026, 6, 2))
    ];

    private static string[] ReadCsv(string? value, string[] fallback) =>
        !string.IsNullOrWhiteSpace(value)
            ? value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : fallback;
}
