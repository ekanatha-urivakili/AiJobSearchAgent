using System.Text;

namespace AiJobSearchAgent.Core;

public sealed class MarkdownReportGenerator
{
    public string Generate(SearchRunResult result, JobSearchCriteria criteria)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"# Daily Job Matches - {criteria.PostedTo:yyyy-MM-dd}");
        builder.AppendLine();
        builder.AppendLine("Search:");
        builder.AppendLine($"- Location: {criteria.Postcode} + {criteria.RadiusMiles} miles");
        builder.AppendLine($"- Posted: {criteria.PostedFrom:yyyy-MM-dd} to {criteria.PostedTo:yyyy-MM-dd}");
        builder.AppendLine($"- Permanent: >= GBP {criteria.MinimumPermanentSalary.Amount:0}/year");
        builder.AppendLine($"- Contract: >= GBP {criteria.MinimumContractDayRate.Amount:0}/day, >= {criteria.MinimumContractMonths} months");
        builder.AppendLine();
        builder.AppendLine("## Source Status");
        builder.AppendLine();
        builder.AppendLine("| Source | Jobs | Warnings |");
        builder.AppendLine("|---|---:|---|");

        foreach (var source in result.SourceResults)
        {
            builder.AppendLine($"| {source.SourceName} | {source.Jobs.Count} | {string.Join("<br>", source.Warnings)} |");
        }

        builder.AppendLine();
        builder.AppendLine("## Strong Matches");
        builder.AppendLine();
        AppendMatches(builder, result.Matches.Where(match => match.Score >= 85));
        builder.AppendLine("## Good Matches");
        builder.AppendLine();
        AppendMatches(builder, result.Matches.Where(match => match.Score is >= 70 and < 85));
        builder.AppendLine("## Rejected Summary");
        builder.AppendLine();
        builder.AppendLine("| Reason | Count |");
        builder.AppendLine("|---|---:|");

        foreach (var rejected in result.RejectedSummary.OrderByDescending(item => item.Value))
        {
            builder.AppendLine($"| {rejected.Key} | {rejected.Value} |");
        }

        return builder.ToString();
    }

    private static void AppendMatches(StringBuilder builder, IEnumerable<JobMatch> matches)
    {
        var index = 1;
        foreach (var match in matches)
        {
            var job = match.Job;
            builder.AppendLine($"### {index}. {job.Title} - {job.Company}");
            builder.AppendLine($"- Source: {job.Source}");
            builder.AppendLine($"- Location: {job.Location} / {job.WorkMode}");
            builder.AppendLine($"- Type: {job.EmploymentType}");
            builder.AppendLine($"- Compensation: {FormatCompensation(job)}");
            builder.AppendLine($"- Posted: {job.PostedDate:yyyy-MM-dd}");
            builder.AppendLine($"- Score: {match.Score}");
            builder.AppendLine($"- Why: {string.Join("; ", match.Reasons)}");
            builder.AppendLine($"- Risks: {string.Join("; ", match.Risks)}");
            builder.AppendLine($"- URL: {job.Url}");
            builder.AppendLine();
            index++;
        }

        if (index == 1)
        {
            builder.AppendLine("No matches.");
            builder.AppendLine();
        }
    }

    private static string FormatCompensation(JobPosting job)
    {
        if (job.EmploymentType == EmploymentType.Permanent)
        {
            return job.SalaryMax is null
                ? $"GBP {job.SalaryMin:0}/year"
                : $"GBP {job.SalaryMin:0}-{job.SalaryMax:0}/year";
        }

        return job.DayRateMax is null
            ? $"GBP {job.DayRateMin:0}/day"
            : $"GBP {job.DayRateMin:0}-{job.DayRateMax:0}/day";
    }
}
