using System.Text;
using System.Text.Json;

namespace AiJobSearchAgent.Core;

public interface IJobReporter
{
    Task ReportAsync(SearchRunResult result, JobSearchCriteria criteria, CancellationToken cancellationToken);
}

public sealed class MarkdownFileJobReporter : IJobReporter
{
    private readonly string outputDirectory;
    private readonly MarkdownReportGenerator generator = new();

    public MarkdownFileJobReporter(string outputDirectory)
    {
        this.outputDirectory = outputDirectory;
    }

    public async Task ReportAsync(SearchRunResult result, JobSearchCriteria criteria, CancellationToken cancellationToken)
    {
        var report = generator.Generate(result, criteria);
        Directory.CreateDirectory(outputDirectory);
        var reportPath = Path.Combine(outputDirectory, $"daily-job-matches-{criteria.PostedTo:yyyy-MM-dd}.md");
        await File.WriteAllTextAsync(reportPath, report, cancellationToken);
    }
}

public sealed class SlackJobReporter : IJobReporter
{
    private readonly HttpClient httpClient;
    private readonly Uri? webhookUrl;

    public SlackJobReporter(HttpClient httpClient, string? webhookUrl)
    {
        this.httpClient = httpClient;
        if (Uri.TryCreate(webhookUrl, UriKind.Absolute, out var uri))
        {
            this.webhookUrl = uri;
        }
    }

    public async Task ReportAsync(SearchRunResult result, JobSearchCriteria criteria, CancellationToken cancellationToken)
    {
        if (webhookUrl is null) return;

        var strongMatches = result.Matches.Where(m => m.Score >= 85).ToArray();
        if (strongMatches.Length == 0) return;

        var payload = new
        {
            text = $"🚀 *{strongMatches.Length} Strong Job Matches Found!*",
            attachments = strongMatches.Select(m => new
            {
                title = $"{m.Job.Title} @ {m.Job.Company}",
                title_link = m.Job.Url.ToString(),
                text = $"Score: {m.Score}\nLocation: {m.Job.Location} ({m.Job.WorkMode})\nWhy: {string.Join("; ", m.Reasons)}",
                color = "#36a64f"
            }).ToArray()
        };

        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        await httpClient.PostAsync(webhookUrl, content, cancellationToken);
    }
}

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
            builder.AppendLine($"### {index}. [{job.Title}]({job.Url}) - {job.Company}");
            builder.AppendLine($"- Source: {job.Source}");
            builder.AppendLine($"- Location: {job.Location} / {job.WorkMode}");
            builder.AppendLine($"- Type: {job.EmploymentType}");
            builder.AppendLine($"- Compensation: {FormatCompensation(job)}");
            builder.AppendLine($"- Posted: {job.PostedDate:yyyy-MM-dd}");
            builder.AppendLine($"- Score: {match.Score}");
            builder.AppendLine($"- Why: {string.Join("; ", match.Reasons)}");
            builder.AppendLine($"- Risks: {string.Join("; ", match.Risks)}");
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
