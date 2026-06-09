namespace AiJobSearchAgent.Core;

public sealed class CvMatchScorer
{
    private readonly DateOnly today;

    public CvMatchScorer(DateOnly? today = null)
    {
        this.today = today ?? DateOnly.FromDateTime(DateTime.UtcNow);
    }

    public JobMatch Score(JobPosting job, CvProfile profile)
    {
        var haystack = $"{job.Title} {job.Description}".ToLowerInvariant();
        var skillMatches = profile.CoreSkills.Where(skill => haystack.Contains(skill, StringComparison.Ordinal)).ToArray();
        var domainMatches = profile.DomainKeywords.Where(keyword => haystack.Contains(keyword, StringComparison.Ordinal)).ToArray();
        var leadershipMatches = profile.LeadershipKeywords.Where(keyword => haystack.Contains(keyword, StringComparison.Ordinal)).ToArray();

        var score = 0;
        score += Math.Min(35, skillMatches.Length * 5);
        score += Math.Min(20, leadershipMatches.Length * 5);
        score += Math.Min(15, domainMatches.Length * 5);
        score += job.WorkMode == WorkMode.Remote || job.DistanceMiles <= 30 ? 5 : 3;
        score += job.PostedDate >= today.AddDays(-3) ? 5 : 3;
        score += CompensationScore(job);

        var reasons = new List<string>();
        if (skillMatches.Length > 0)
        {
            reasons.Add($"Tech stack match: {string.Join(", ", skillMatches.Take(6))}");
        }

        if (leadershipMatches.Length > 0)
        {
            reasons.Add($"Seniority match: {string.Join(", ", leadershipMatches.Take(4))}");
        }

        if (domainMatches.Length > 0)
        {
            reasons.Add($"Domain match: {string.Join(", ", domainMatches.Take(4))}");
        }

        reasons.Add(job.EmploymentType == EmploymentType.Permanent
            ? $"Permanent compensation visible up to GBP {job.SalaryMax ?? job.SalaryMin:0}"
            : $"Contract rate visible up to GBP {job.DayRateMax ?? job.DayRateMin:0}/day");

        var risks = new List<string>();
        if (job.WorkMode == WorkMode.Office && job.DistanceMiles > 30)
        {
            risks.Add("Office-based role may require commute review");
        }

        if (skillMatches.Length < 3)
        {
            risks.Add("Limited explicit overlap with CV keywords");
        }

        return new(job, Math.Min(100, score), score >= 70, reasons, risks);
    }

    private static int CompensationScore(JobPosting job)
    {
        if (job.EmploymentType == EmploymentType.Permanent)
        {
            var salary = Math.Max(job.SalaryMin ?? 0, job.SalaryMax ?? 0);
            return salary >= 90000 ? 10 : 7;
        }

        var rate = Math.Max(job.DayRateMin ?? 0, job.DayRateMax ?? 0);
        return rate >= 550 ? 10 : 7;
    }
}
