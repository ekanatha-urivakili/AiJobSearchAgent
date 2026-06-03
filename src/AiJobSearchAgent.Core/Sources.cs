using Google.Apis.Auth.OAuth2;
using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using Google.Apis.Services;
using HtmlAgilityPack;

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

public sealed class GmailAlertJobSourceAdapter : IJobSourceAdapter
{
    private readonly string credentialsJson;
    private readonly string query;

    public GmailAlertJobSourceAdapter(string? credentialsJson, string query = "label:job-alerts is:unread")
    {
        this.credentialsJson = credentialsJson ?? string.Empty;
        this.query = query;
    }

    public string SourceName => "Gmail Alerts";

    public async Task<SourceFetchResult> FetchAsync(JobSearchCriteria criteria, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(credentialsJson))
        {
            return new SourceFetchResult(SourceName, Array.Empty<JobPosting>(), ["Gmail credentials not configured."]);
        }

        try
        {
            var credential = GoogleCredential.FromJson(credentialsJson)
                .CreateScoped(GmailService.Scope.GmailReadonly);

            var service = new GmailService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "AiJobSearchAgent"
            });

            var listRequest = service.Users.Messages.List("me");
            listRequest.Q = query;
            var response = await listRequest.ExecuteAsync(cancellationToken);

            if (response.Messages == null || response.Messages.Count == 0)
            {
                return new SourceFetchResult(SourceName, Array.Empty<JobPosting>(), Array.Empty<string>());
            }

            var jobs = new List<JobPosting>();
            var warnings = new List<string>();

            foreach (var msgSummary in response.Messages)
            {
                var message = await service.Users.Messages.Get("me", msgSummary.Id).ExecuteAsync(cancellationToken);
                var body = GetMessageBody(message);

                if (string.IsNullOrWhiteSpace(body)) continue;

                var extractedJobs = ParseAlertEmail(body, message.Id);
                jobs.AddRange(extractedJobs);
            }

            return new SourceFetchResult(SourceName, jobs, warnings);
        }
        catch (Exception ex)
        {
            return new SourceFetchResult(SourceName, Array.Empty<JobPosting>(), [$"Gmail error: {ex.Message}"]);
        }
    }

    private static string GetMessageBody(Message message)
    {
        if (message.Payload.Body.Data != null)
        {
            return DecodeBase64(message.Payload.Body.Data);
        }

        if (message.Payload.Parts != null)
        {
            foreach (var part in message.Payload.Parts)
            {
                if (part.MimeType == "text/html" && part.Body.Data != null)
                {
                    return DecodeBase64(part.Body.Data);
                }
            }
        }

        return string.Empty;
    }

    private static string DecodeBase64(string base64)
    {
        var data = Convert.FromBase64String(base64.Replace('-', '+').Replace('_', '/'));
        return System.Text.Encoding.UTF8.GetString(data);
    }

    private static IReadOnlyCollection<JobPosting> ParseAlertEmail(string html, string messageId)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var jobs = new List<JobPosting>();
        
        var links = doc.DocumentNode.SelectNodes("//a");
        if (links == null) return jobs;

        foreach (var link in links)
        {
            var urlStr = link.GetAttributeValue("href", "");
            if (string.IsNullOrEmpty(urlStr) || !Uri.TryCreate(urlStr, UriKind.Absolute, out var url)) continue;

            var text = link.InnerText.Trim();
            
            if (urlStr.Contains("/jobs/") || urlStr.Contains("/job/"))
            {
                if (text.Length > 15)
                {
                    jobs.Add(new JobPosting(
                        "GmailAlert",
                        $"{messageId}_{Guid.NewGuid():N}",
                        url,
                        text,
                        "Unknown",
                        "Unknown",
                        0,
                        EmploymentType.Permanent,
                        WorkMode.Hybrid,
                        null, null, null, null, null,
                        DateOnly.FromDateTime(DateTime.Today),
                        "Job alert match. Details in link."));
                }
            }
        }

        return jobs;
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
