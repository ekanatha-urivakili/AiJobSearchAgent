namespace AiJobSearchAgent.Core;

public sealed class JobFilterEngine
{
    public FilterDecision Evaluate(JobPosting job, JobSearchCriteria criteria)
    {
        if (job.PostedDate < criteria.PostedFrom || job.PostedDate > criteria.PostedTo)
        {
            return new(false, "Old posting");
        }

        if (!criteria.EmploymentTypes.Contains(job.EmploymentType))
        {
            return new(false, "Employment type mismatch");
        }

        if (!criteria.WorkModes.Contains(job.WorkMode))
        {
            return new(false, "Work mode mismatch");
        }

        if (!IsTitleMatch(job.Title, criteria.Titles))
        {
            return new(false, "Title mismatch");
        }

        if (job.WorkMode != WorkMode.Remote && job.DistanceMiles > criteria.RadiusMiles)
        {
            return new(false, "Outside radius");
        }

        if (job.EmploymentType == EmploymentType.Permanent)
        {
            var bestSalary = Math.Max(job.SalaryMin ?? 0, job.SalaryMax ?? 0);
            return bestSalary >= criteria.MinimumPermanentSalary.Amount
                ? new(true, "Accepted")
                : new(false, "Below salary threshold");
        }

        var bestDayRate = Math.Max(job.DayRateMin ?? 0, job.DayRateMax ?? 0);
        if (bestDayRate < criteria.MinimumContractDayRate.Amount)
        {
            return new(false, "Below day rate threshold");
        }

        if ((job.ContractMonths ?? 0) < criteria.MinimumContractMonths)
        {
            return new(false, "Below contract duration threshold");
        }

        return new(true, "Accepted");
    }

    private static bool IsTitleMatch(string title, IReadOnlyCollection<string> targets)
    {
        var normalized = Normalize(title);
        if (targets.Any(target => normalized.Contains(Normalize(target), StringComparison.Ordinal)))
        {
            return true;
        }

        var seniorSoftware = normalized.Contains("senior", StringComparison.Ordinal)
            && normalized.Contains("software", StringComparison.Ordinal)
            && (normalized.Contains("engineer", StringComparison.Ordinal) || normalized.Contains("developer", StringComparison.Ordinal));

        var leadDeveloper = normalized.Contains("lead", StringComparison.Ordinal)
            && (normalized.Contains("developer", StringComparison.Ordinal) || normalized.Contains("engineer", StringComparison.Ordinal));

        var fullStack = normalized.Contains("senior", StringComparison.Ordinal)
            && (normalized.Contains("fullstack", StringComparison.Ordinal) || normalized.Contains("full stack", StringComparison.Ordinal));

        return seniorSoftware || leadDeveloper || fullStack;
    }

    private static string Normalize(string value) => value.Trim().ToLowerInvariant().Replace("-", " ");
}
