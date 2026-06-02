using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Web;
using AiJobSearchAgent.Core;

namespace AiJobSearchAgent.McpServer;

public sealed class ReedApiJobSourceAdapter : IJobSourceAdapter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient httpClient;
    private readonly string apiKey;

    public ReedApiJobSourceAdapter(HttpClient httpClient, string apiKey)
    {
        this.httpClient = httpClient;
        this.apiKey = apiKey;
    }

    public string SourceName => "Reed";

    public async Task<SourceFetchResult> FetchAsync(JobSearchCriteria criteria, CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        var jobs = new List<JobPosting>();

        foreach (var title in criteria.Titles)
        {
            var searchUri = BuildSearchUri(criteria, title);
            using var request = CreateRequest(searchUri);
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                warnings.Add($"Reed API search failed with {(int)response.StatusCode} for '{title}'.");
                continue;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var search = await JsonSerializer.DeserializeAsync<ReedSearchResponse>(stream, JsonOptions, cancellationToken);
            foreach (var item in search?.Results ?? [])
            {
                var jobId = item.JobId?.ToString();
                if (string.IsNullOrWhiteSpace(jobId))
                {
                    warnings.Add("Reed search result skipped because jobId was missing.");
                    continue;
                }

                // Only call the details endpoint when the search snippet description is too short to score reliably.
                ReedJobDto candidate = item;
                if ((item.JobDescription?.Length ?? 0) < 200)
                {
                    var details = await FetchJobDetailsAsync(jobId, cancellationToken);
                    if (details is not null)
                    {
                        candidate = details;
                    }
                }

                var job = MapJob(candidate, criteria);
                if (job is null)
                {
                    warnings.Add($"Reed job {jobId} skipped because required fields were missing.");
                    continue;
                }

                jobs.Add(job);
            }
        }

        return new(SourceName, jobs, warnings);
    }

    public async Task<JobPosting?> FetchJobAsync(string sourceJobId, CancellationToken cancellationToken)
    {
        var details = await FetchJobDetailsAsync(sourceJobId, cancellationToken);
        return details is null ? null : MapJob(details, Defaults.CreateCriteria(DateOnly.FromDateTime(DateTime.UtcNow)));
    }

    private async Task<ReedJobDto?> FetchJobDetailsAsync(string jobId, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(new Uri($"https://www.reed.co.uk/api/1.0/jobs/{HttpUtility.UrlEncode(jobId)}"));
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<ReedJobDto>(stream, JsonOptions, cancellationToken);
    }

    private HttpRequestMessage CreateRequest(Uri uri)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{apiKey}:"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private static Uri BuildSearchUri(JobSearchCriteria criteria, string title)
    {
        var query = HttpUtility.ParseQueryString(string.Empty);
        query["keywords"] = title;
        query["locationName"] = criteria.Postcode;
        query["distanceFromLocation"] = criteria.RadiusMiles.ToString();
        query["resultsToTake"] = "25";
        query["resultsToSkip"] = "0";
        // minimumSalary intentionally omitted: criteria contains both permanent salary and contract day-rate
        // thresholds which are incompatible units. Local JobFilterEngine applies the correct threshold per type.
        return new Uri($"https://www.reed.co.uk/api/1.0/search?{query}");
    }

    private static JobPosting? MapJob(ReedJobDto item, JobSearchCriteria criteria)
    {
        var jobId = item.JobId?.ToString();
        if (string.IsNullOrWhiteSpace(jobId)
            || string.IsNullOrWhiteSpace(item.JobTitle)
            || string.IsNullOrWhiteSpace(item.EmployerName)
            || string.IsNullOrWhiteSpace(item.LocationName)
            || string.IsNullOrWhiteSpace(item.JobUrl)
            || !Uri.TryCreate(item.JobUrl, UriKind.Absolute, out var url))
        {
            return null;
        }

        var description = item.JobDescription ?? string.Empty;
        var employmentType = InferEmploymentType(item, description);
        var workMode = InferWorkMode($"{item.JobTitle} {item.LocationName} {description}");
        var postedDate = ParseDate(item.Date) ?? criteria.PostedTo;
        var months = employmentType == EmploymentType.Contract ? InferContractMonths(description) : null;
        var salaryMin = employmentType == EmploymentType.Permanent ? item.MinimumSalary : null;
        var salaryMax = employmentType == EmploymentType.Permanent ? item.MaximumSalary : null;
        var dayRateMin = employmentType == EmploymentType.Contract ? item.MinimumSalary : null;
        var dayRateMax = employmentType == EmploymentType.Contract ? item.MaximumSalary : null;

        return new(
            "Reed",
            jobId,
            url,
            item.JobTitle,
            item.EmployerName,
            item.LocationName,
            item.Distance ?? criteria.RadiusMiles,
            employmentType,
            workMode,
            salaryMin,
            salaryMax,
            dayRateMin,
            dayRateMax,
            months,
            postedDate,
            description);
    }

    private static EmploymentType InferEmploymentType(ReedJobDto item, string description)
    {
        // Prefer the explicit JobType field from the API before falling back to text heuristics.
        var jobType = item.JobType?.ToLowerInvariant() ?? string.Empty;
        if (jobType.Contains("contract", StringComparison.Ordinal)
            || jobType.Contains("freelance", StringComparison.Ordinal)
            || jobType.Contains("interim", StringComparison.Ordinal))
        {
            return EmploymentType.Contract;
        }

        if (jobType.Contains("permanent", StringComparison.Ordinal)
            || jobType.Contains("full time", StringComparison.Ordinal))
        {
            return EmploymentType.Permanent;
        }

        // Fall back to text search in title and description.
        var haystack = $"{item.JobTitle} {description}".ToLowerInvariant();
        return haystack.Contains("contract", StringComparison.Ordinal)
            ? EmploymentType.Contract
            : EmploymentType.Permanent;
    }

    private static WorkMode InferWorkMode(string value)
    {
        var normalized = value.ToLowerInvariant();
        if (normalized.Contains("remote", StringComparison.Ordinal))
        {
            return WorkMode.Remote;
        }

        return normalized.Contains("hybrid", StringComparison.Ordinal) ? WorkMode.Hybrid : WorkMode.Office;
    }

    private static int? InferContractMonths(string description)
    {
        var normalized = description.ToLowerInvariant();
        for (var months = 3; months <= 24; months++)
        {
            if (normalized.Contains($"{months} month", StringComparison.Ordinal))
            {
                return months;
            }
        }

        return null;
    }

    private static DateOnly? ParseDate(DateTimeOffset? value) =>
        value is null ? null : DateOnly.FromDateTime(value.Value.UtcDateTime);

    private sealed record ReedSearchResponse(IReadOnlyCollection<ReedJobDto>? Results);

    private sealed record ReedJobDto(
        int? JobId,
        string? JobTitle,
        string? EmployerName,
        string? LocationName,
        decimal? MinimumSalary,
        decimal? MaximumSalary,
        string? Currency,
        string? JobDescription,
        string? JobUrl,
        string? JobType,
        double? Distance,
        DateTimeOffset? Date);
}
