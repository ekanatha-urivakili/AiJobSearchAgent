using AiJobSearchAgent.Core;

var tests = new (string Name, Action Test)[]
{
    ("Permanent jobs below GBP 75,000 are rejected", PermanentBelowSalaryIsRejected),
    ("Contract jobs below GBP 400/day are rejected", ContractBelowDayRateIsRejected),
    ("Contract jobs shorter than 6 months are rejected", ShortContractIsRejected),
    ("Remote jobs outside radius are accepted when compensation qualifies", RemoteOutsideRadiusIsAccepted),
    ("CV scorer recommends strong .NET React AWS jobs", StrongMatchIsRecommended)
};

var failures = new List<string>();

foreach (var test in tests)
{
    try
    {
        test.Test();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception ex)
    {
        failures.Add($"{test.Name}: {ex.Message}");
        Console.WriteLine($"FAIL {test.Name}: {ex.Message}");
    }
}

if (failures.Count > 0)
{
    Console.WriteLine();
    Console.WriteLine("Failures:");
    foreach (var failure in failures)
    {
        Console.WriteLine(failure);
    }

    return 1;
}

return 0;

static void PermanentBelowSalaryIsRejected()
{
    var criteria = Defaults.CreateCriteria(new DateOnly(2026, 5, 30));
    var job = CreateJob(EmploymentType.Permanent, WorkMode.Hybrid, salaryMin: 70000, salaryMax: 74000);

    var decision = new JobFilterEngine().Evaluate(job, criteria);

    AssertFalse(decision.Accepted);
    AssertEqual("Below salary threshold", decision.Reason);
}

static void ContractBelowDayRateIsRejected()
{
    var criteria = Defaults.CreateCriteria(new DateOnly(2026, 5, 30));
    var job = CreateJob(EmploymentType.Contract, WorkMode.Hybrid, dayRateMin: 350, dayRateMax: 375, contractMonths: 6);

    var decision = new JobFilterEngine().Evaluate(job, criteria);

    AssertFalse(decision.Accepted);
    AssertEqual("Below day rate threshold", decision.Reason);
}

static void ShortContractIsRejected()
{
    var criteria = Defaults.CreateCriteria(new DateOnly(2026, 5, 30));
    var job = CreateJob(EmploymentType.Contract, WorkMode.Hybrid, dayRateMin: 450, dayRateMax: 500, contractMonths: 3);

    var decision = new JobFilterEngine().Evaluate(job, criteria);

    AssertFalse(decision.Accepted);
    AssertEqual("Below contract duration threshold", decision.Reason);
}

static void RemoteOutsideRadiusIsAccepted()
{
    var criteria = Defaults.CreateCriteria(new DateOnly(2026, 5, 30));
    var job = CreateJob(EmploymentType.Permanent, WorkMode.Remote, distanceMiles: 200, salaryMin: 80000, salaryMax: 90000);

    var decision = new JobFilterEngine().Evaluate(job, criteria);

    AssertTrue(decision.Accepted);
}

static void StrongMatchIsRecommended()
{
    var job = CreateJob(EmploymentType.Permanent, WorkMode.Hybrid, salaryMin: 85000, salaryMax: 95000);
    var match = new CvMatchScorer().Score(job, Defaults.CreateCvProfile());

    AssertTrue(match.Recommended);
}

static JobPosting CreateJob(
    EmploymentType employmentType,
    WorkMode workMode,
    double distanceMiles = 10,
    decimal? salaryMin = null,
    decimal? salaryMax = null,
    decimal? dayRateMin = null,
    decimal? dayRateMax = null,
    int? contractMonths = null) =>
    new(
        "Test",
        Guid.NewGuid().ToString("N"),
        new Uri("https://example.com/job"),
        "Senior Software Engineer",
        "Example Ltd",
        "Milton Keynes",
        distanceMiles,
        employmentType,
        workMode,
        salaryMin,
        salaryMax,
        dayRateMin,
        dayRateMax,
        contractMonths,
        new DateOnly(2026, 5, 29),
        "Senior role using C#, ASP.NET Core, Web API, React, TypeScript, AWS, Docker, SQL Server, microservices, payments, e-commerce, Agile, architecture and code review.");

static void AssertTrue(bool value)
{
    if (!value)
    {
        throw new InvalidOperationException("Expected true.");
    }
}

static void AssertFalse(bool value)
{
    if (value)
    {
        throw new InvalidOperationException("Expected false.");
    }
}

static void AssertEqual<T>(T expected, T actual)
    where T : notnull
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }
}
