using Google.Apis.Auth.OAuth2;
using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using Google.Apis.Services;
using HtmlAgilityPack;
using System.Text;

namespace AiJobSearchAgent.Core;

internal static class GmailCredentialFactory
{
    public static GoogleCredential CreateDelegated(string credentialsJson, string mailbox)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(credentialsJson));
        var serviceAccount = ServiceAccountCredential.FromServiceAccountData(stream);
        return serviceAccount.ToGoogleCredential()
            .CreateScoped(GmailService.Scope.GmailReadonly)
            .CreateWithUser(mailbox);
    }
}

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
    private readonly string userEmail;

    public GmailAlertJobSourceAdapter(string? credentialsJson, string query = "label:job-alerts is:unread", string? userEmail = null)
    {
        this.credentialsJson = credentialsJson ?? string.Empty;
        this.query = query;
        this.userEmail = userEmail ?? string.Empty;
    }

    public string SourceName => "Gmail Alerts";

    public async Task<SourceFetchResult> FetchAsync(JobSearchCriteria criteria, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(credentialsJson))
        {
            return new SourceFetchResult(SourceName, Array.Empty<JobPosting>(), ["Gmail credentials not configured."]);
        }

        if (string.IsNullOrWhiteSpace(userEmail))
        {
            return new SourceFetchResult(SourceName, Array.Empty<JobPosting>(), ["GMAIL_USER_EMAIL is not configured. Service-account Gmail access requires a delegated mailbox user."]);
        }

        try
        {
            var mailbox = userEmail.Trim();
            var credential = GmailCredentialFactory.CreateDelegated(credentialsJson, mailbox);

            var service = new GmailService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "AiJobSearchAgent"
            });

            var listRequest = service.Users.Messages.List(mailbox);
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
                var message = await service.Users.Messages.Get(mailbox, msgSummary.Id).ExecuteAsync(cancellationToken);
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
        ])
    ];
}

/// <summary>
/// Generic in-memory adapter for jobs ingested via external MCP plugins (Indeed, Dice, ZipRecruiter, etc.).
/// Claude fetches from the plugin, normalises results into <see cref="JobPosting"/>, then calls the
/// corresponding <c>jobs.ingest_*</c> tool so the jobs flow through the standard filter/score pipeline.
/// </summary>
public sealed class PluginJobSourceAdapter : IJobSourceAdapter
{
    private readonly System.Collections.Concurrent.ConcurrentBag<JobPosting> buffer = new();

    public PluginJobSourceAdapter(string sourceName) => SourceName = sourceName;

    public string SourceName { get; }

    /// <summary>Adds a batch of pre-normalised jobs to the buffer.</summary>
    public void Ingest(IEnumerable<JobPosting> jobs)
    {
        foreach (var job in jobs)
            buffer.Add(job);
    }

    /// <summary>Clears all previously ingested jobs (call before a fresh ingest cycle).</summary>
    public void Clear() => buffer.Clear();

    public int Count => buffer.Count;

    public Task<SourceFetchResult> FetchAsync(JobSearchCriteria criteria, CancellationToken cancellationToken)
    {
        var jobs = buffer.ToArray();
        if (jobs.Length == 0)
        {
            return Task.FromResult(new SourceFetchResult(
                SourceName,
                Array.Empty<JobPosting>(),
                [$"No jobs ingested yet — call jobs.ingest_{SourceName.ToLowerInvariant().Replace(" ", "_")} before jobs.search."]));
        }

        return Task.FromResult(new SourceFetchResult(SourceName, jobs, Array.Empty<string>()));
    }
}

/// <summary>Backward-compatible alias — use <see cref="PluginJobSourceAdapter"/> for new sources.</summary>
public sealed class IndeedDirectJobSourceAdapter : IJobSourceAdapter
{
    private readonly PluginJobSourceAdapter inner = new("Indeed Direct");
    public string SourceName => inner.SourceName;
    public void Ingest(IEnumerable<JobPosting> jobs) => inner.Ingest(jobs);
    public void Clear() => inner.Clear();
    public int Count => inner.Count;
    public Task<SourceFetchResult> FetchAsync(JobSearchCriteria criteria, CancellationToken ct) =>
        inner.FetchAsync(criteria, ct);
}
