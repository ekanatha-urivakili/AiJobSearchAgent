namespace AiJobSearchAgent.Core;

public sealed class JobSearchOrchestrator
{
    private readonly IReadOnlyCollection<IJobSourceAdapter> sources;
    private readonly SourcePolicyGuard policyGuard;
    private readonly JobFilterEngine filterEngine;
    private readonly CvMatchScorer scorer;

    public JobSearchOrchestrator(
        IReadOnlyCollection<IJobSourceAdapter> sources,
        SourcePolicyGuard policyGuard,
        JobFilterEngine filterEngine,
        CvMatchScorer scorer)
    {
        this.sources = sources;
        this.policyGuard = policyGuard;
        this.filterEngine = filterEngine;
        this.scorer = scorer;
    }

    public async Task<SearchRunResult> RunAsync(
        JobSearchCriteria criteria,
        CvProfile profile,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var sourceResults = new List<SourceFetchResult>();
        var rejectedSummary = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var candidateJobs = new List<JobPosting>();

        foreach (var source in sources)
        {
            var policyDecision = policyGuard.CanFetch(source.SourceName);
            if (!policyDecision.Accepted)
            {
                AddRejected(rejectedSummary, $"{source.SourceName}: {policyDecision.Reason}");
                continue;
            }

            var result = await source.FetchAsync(criteria, cancellationToken);
            sourceResults.Add(result);
            candidateJobs.AddRange(result.Jobs);
        }

        var uniqueJobs = Deduplicate(candidateJobs);
        var matches = new List<JobMatch>();

        foreach (var job in uniqueJobs)
        {
            var decision = filterEngine.Evaluate(job, criteria);
            if (!decision.Accepted)
            {
                AddRejected(rejectedSummary, decision.Reason);
                continue;
            }

            matches.Add(scorer.Score(job, profile));
        }

        return new SearchRunResult(
            startedAt,
            DateTimeOffset.UtcNow,
            sourceResults,
            matches.OrderByDescending(match => match.Score).ToArray(),
            rejectedSummary);
    }

    private static IReadOnlyCollection<JobPosting> Deduplicate(IEnumerable<JobPosting> jobs) =>
        jobs
            .GroupBy(job => $"{job.Company}|{job.Title}|{job.Location}".ToLowerInvariant())
            .Select(group => group.OrderByDescending(job => job.PostedDate).First())
            .ToArray();

    private static void AddRejected(IDictionary<string, int> rejectedSummary, string reason)
    {
        rejectedSummary.TryGetValue(reason, out var count);
        rejectedSummary[reason] = count + 1;
    }
}
