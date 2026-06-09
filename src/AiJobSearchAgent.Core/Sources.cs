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

public sealed class IndeedAlertJobSourceAdapter : IJobSourceAdapter
{
    // Salary pattern: £80,000 - £90,000 a year | £450 - £550 a day | £80k
    private static readonly System.Text.RegularExpressions.Regex SalaryRegex = new(
        @"£([\d,]+)k?\s*(?:[-–]\s*£([\d,]+)k?)?\s*(a\s+year|per\s+annum|p\.?a\.?|/yr|a\s+day|per\s+day|/day)?",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    // UK location signals
    private static readonly System.Text.RegularExpressions.Regex LocationRegex = new(
        @"(?:London|Manchester|Birmingham|Leeds|Edinburgh|Bristol|Sheffield|Cardiff|Liverpool|Nottingham|" +
        @"Leicester|Milton Keynes|Oxford|Cambridge|Reading|Southampton|Brighton|Newcastle|Glasgow|" +
        @"Coventry|Luton|Bedford|Northampton|Derby|Norwich|Portsmouth|York|Aberdeen|Dundee|" +
        @"Remote|Hybrid|United Kingdom|England|UK)(?:[^<\n]{0,40})?",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    private readonly string? credentialsJson;
    private readonly string query;
    private readonly string userEmail;

    public IndeedAlertJobSourceAdapter(string? credentialsJson, string? query = null, string? userEmail = null)
    {
        this.credentialsJson = credentialsJson;
        this.query = string.IsNullOrWhiteSpace(query)
            ? "from:jobalerts-noreply@indeed.com is:unread"
            : query;
        this.userEmail = userEmail ?? string.Empty;
    }

    public string SourceName => "Indeed UK";

    public async Task<SourceFetchResult> FetchAsync(JobSearchCriteria criteria, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(credentialsJson))
        {
            return new SourceFetchResult(SourceName, Array.Empty<JobPosting>(), ["Indeed alert credentials not configured."]);
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
            var listResponse = await listRequest.ExecuteAsync(cancellationToken);

            if (listResponse.Messages == null || listResponse.Messages.Count == 0)
            {
                return new SourceFetchResult(SourceName, Array.Empty<JobPosting>(), Array.Empty<string>());
            }

            var jobs = new List<JobPosting>();
            var warnings = new List<string>();
            var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var msgSummary in listResponse.Messages)
            {
                var message = await service.Users.Messages.Get(mailbox, msgSummary.Id).ExecuteAsync(cancellationToken);
                var body = GetMessageBody(message);
                if (string.IsNullOrWhiteSpace(body)) continue;

                var extracted = ParseAlertEmail(body, criteria);
                foreach (var job in extracted)
                {
                    if (seenIds.Add(job.SourceJobId))
                        jobs.Add(job);
                }
            }

            return new SourceFetchResult(SourceName, jobs, warnings);
        }
        catch (Exception ex)
        {
            return new SourceFetchResult(SourceName, Array.Empty<JobPosting>(), [$"Indeed alert error: {ex.Message}"]);
        }
    }

    // ── HTML parsing ─────────────────────────────────────────────────────────

    public IReadOnlyCollection<JobPosting> ParseAlertEmail(string html, JobSearchCriteria criteria)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var jobs = new List<JobPosting>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var links = doc.DocumentNode.SelectNodes("//a[@href]");
        if (links == null) return jobs;

        foreach (var link in links)
        {
            var href = link.GetAttributeValue("href", "");

            // Must be an Indeed URL with a job key
            if (!href.Contains("indeed.com", StringComparison.OrdinalIgnoreCase)) continue;
            if (!Uri.TryCreate(href, UriKind.Absolute, out _)) continue;

            var jk = ExtractJobKey(href);
            if (string.IsNullOrWhiteSpace(jk)) continue;
            if (!seen.Add(jk)) continue;

            var title = HtmlEntity.DeEntitize(link.InnerText).Trim();
            // Filter out navigation/button links that don't look like job titles
            if (title.Length < 8 || title.Contains("http", StringComparison.OrdinalIgnoreCase)) continue;

            var container = FindJobContainer(link);
            var containerText = container != null
                ? HtmlEntity.DeEntitize(container.InnerText)
                : string.Empty;

            var company = ExtractCompany(container, title);
            var location = ExtractLocation(containerText);
            var (salaryMin, salaryMax, isDayRate) = ExtractSalary(containerText);
            var description = BuildDescription(containerText, title);
            var employmentType = isDayRate ? EmploymentType.Contract : EmploymentType.Permanent;
            var workMode = InferWorkMode($"{title} {location} {description}");
            var months = employmentType == EmploymentType.Contract ? InferContractMonths(description) : (int?)null;

            // Canonical URL uses the stable jk parameter
            var canonicalUrl = new Uri($"https://uk.indeed.com/viewjob?jk={jk}");

            jobs.Add(new JobPosting(
                SourceName,
                jk,
                canonicalUrl,
                title,
                string.IsNullOrWhiteSpace(company) ? "Unknown" : company,
                string.IsNullOrWhiteSpace(location) ? "Unknown" : location,
                0, // Distance not available from email alert
                employmentType,
                workMode,
                employmentType == EmploymentType.Permanent ? salaryMin : null,
                employmentType == EmploymentType.Permanent ? salaryMax : null,
                employmentType == EmploymentType.Contract ? salaryMin : null,
                employmentType == EmploymentType.Contract ? salaryMax : null,
                months,
                DateOnly.FromDateTime(DateTime.Today),
                description));
        }

        return jobs;
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    /// <summary>Extracts the stable Indeed job key (jk=...) from any Indeed URL variant.</summary>
    public static string? ExtractJobKey(string url)
    {
        // Handle Indeed redirect URLs: /rc/clk?jk=XXX and canonical /viewjob?jk=XXX
        var queryStart = url.IndexOf('?');
        if (queryStart < 0) return null;

        var query = url[(queryStart + 1)..];
        foreach (var part in query.Split('&'))
        {
            var kv = part.Split('=', 2);
            if (kv.Length == 2 && kv[0].Equals("jk", StringComparison.OrdinalIgnoreCase))
                return Uri.UnescapeDataString(kv[1]);
        }

        return null;
    }

    private static HtmlNode? FindJobContainer(HtmlNode link)
    {
        // Walk up the DOM to find a table cell, list item, or substantial div
        var node = link.ParentNode;
        for (var depth = 0; depth < 7 && node != null; depth++)
        {
            var tag = node.Name.ToLowerInvariant();
            if (tag is "td" or "li" or "article") return node;
            if (tag == "div" && (node.InnerText?.Length ?? 0) > 60) return node;
            node = node.ParentNode;
        }
        return link.ParentNode;
    }

    private static string ExtractCompany(HtmlNode? container, string title)
    {
        if (container == null) return string.Empty;

        foreach (var node in container.ChildNodes.Concat(
            container.SelectNodes(".//span | .//div | .//p") ?? Enumerable.Empty<HtmlNode>()))
        {
            var text = HtmlEntity.DeEntitize(node.InnerText ?? "").Trim();
            var firstLine = text.Split('\n', '\r')[0].Trim();

            // Skip the title itself and empty/too-long/salary nodes
            if (string.IsNullOrWhiteSpace(firstLine)) continue;
            if (firstLine.Equals(title, StringComparison.OrdinalIgnoreCase)) continue;
            if (firstLine.Length > 100 || firstLine.Length < 3) continue;
            if (firstLine.Contains('£') || firstLine.Contains("http")) continue;
            if (firstLine.StartsWith("View ", StringComparison.OrdinalIgnoreCase)) continue;

            return firstLine;
        }

        return string.Empty;
    }

    private static string ExtractLocation(string text)
    {
        var match = LocationRegex.Match(text);
        return match.Success ? match.Value.Trim().Split('\n')[0].Trim() : string.Empty;
    }

    public static (decimal? min, decimal? max, bool isDayRate) ExtractSalary(string text)
    {
        var match = SalaryRegex.Match(text);
        if (!match.Success) return (null, null, false);

        var rawMin = match.Groups[1].Value.Replace(",", "");
        if (!decimal.TryParse(rawMin, out var min)) return (null, null, false);

        decimal? max = null;
        if (match.Groups[2].Success)
        {
            var rawMax = match.Groups[2].Value.Replace(",", "");
            if (decimal.TryParse(rawMax, out var maxVal))
                max = maxVal;
        }

        if (match.Value.Contains('k', StringComparison.OrdinalIgnoreCase))
        {
            min *= 1000;
            if (max.HasValue) max *= 1000;
        }

        var period = match.Groups[3].Value.ToLowerInvariant();
        var isDayRate = period.Contains("day");

        return (min, max, isDayRate);
    }

    private static string BuildDescription(string containerText, string title)
    {
        if (string.IsNullOrWhiteSpace(containerText))
            return "Sourced from Indeed job alert. Visit link for full details.";

        var trimmed = containerText.Trim();
        const int maxLen = 400;
        return trimmed.Length > maxLen
            ? trimmed[..maxLen].Trim() + "…"
            : trimmed;
    }

    private static WorkMode InferWorkMode(string value)
    {
        var v = value.ToLowerInvariant();
        if (v.Contains("remote")) return WorkMode.Remote;
        return v.Contains("hybrid") ? WorkMode.Hybrid : WorkMode.Office;
    }

    private static int? InferContractMonths(string description)
    {
        var v = description.ToLowerInvariant();
        for (var m = 3; m <= 24; m++)
            if (v.Contains($"{m} month")) return m;
        return null;
    }

    /// <summary>Test-only helper: parses an HTML string as if it were an Indeed alert email body.</summary>
    public static IReadOnlyList<JobPosting> ParseFixture(string html, JobSearchCriteria criteria)
    {
        var adapter = new IndeedAlertJobSourceAdapter(null);
        return adapter.ParseAlertEmail(html, criteria).ToList();
    }

    private static string GetMessageBody(Message message)
    {
        if (message.Payload?.Body?.Data != null)
            return DecodeBase64(message.Payload.Body.Data);

        if (message.Payload?.Parts != null)
        {
            foreach (var part in message.Payload.Parts)
            {
                if (part.MimeType == "text/html" && part.Body?.Data != null)
                    return DecodeBase64(part.Body.Data);
            }
        }

        return string.Empty;
    }

    private static string DecodeBase64(string base64)
    {
        var data = Convert.FromBase64String(base64.Replace('-', '+').Replace('_', '/'));
        return System.Text.Encoding.UTF8.GetString(data);
    }

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
