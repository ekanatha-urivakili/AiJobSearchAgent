namespace AiJobSearchAgent.Core;

public static class Defaults
{
    public static JobSearchCriteria CreateCriteria(DateOnly today) =>
        new(
            Titles:
            [
                "Senior Software Engineer",
                "Senior Fullstack Engineer",
                "Lead Developer",
                "Senior Software Developer"
            ],
            Postcode: "MK4 4QG",
            RadiusMiles: 50,
            PostedFrom: today.AddDays(-7),
            PostedTo: today,
            EmploymentTypes: [EmploymentType.Permanent, EmploymentType.Contract],
            WorkModes: [WorkMode.Remote, WorkMode.Hybrid, WorkMode.Office],
            MinimumPermanentSalary: Money.Gbp(75000),
            MinimumContractDayRate: Money.Gbp(400),
            MinimumContractMonths: 6);

    public static CvProfile CreateCvProfile() =>
        new(
            Name: "Ekanatha Reddy Urivakili",
            LocationPostcode: "MK4 4QG",
            CoreSkills:
            [
                "c#",
                "asp.net core",
                "web api",
                "react",
                "typescript",
                "javascript",
                "php",
                "aws",
                "docker",
                "sql server",
                "postgresql",
                "mysql",
                "mongodb",
                "microservices",
                "cqrs",
                "rest"
            ],
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

    public static IReadOnlyCollection<SourcePolicy> CreateSourcePolicies() =>
    [
        new("Indeed UK", FetchMode.Disabled, Enabled: false, TimeSpan.FromSeconds(10), new DateOnly(2026, 5, 30)),
        new("Reed", FetchMode.AlertInbox, Enabled: true, TimeSpan.FromSeconds(3), new DateOnly(2026, 5, 30)),
        new("JobServe", FetchMode.AlertInbox, Enabled: true, TimeSpan.FromSeconds(3), new DateOnly(2026, 5, 30))
    ];
}
