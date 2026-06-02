using AiJobSearchAgent.Core;

namespace AiJobSearchAgent.McpServer;

public sealed class PolicyBlockedSourceAdapter : IJobSourceAdapter
{
    private readonly string warning;

    public PolicyBlockedSourceAdapter(string sourceName, string warning)
    {
        SourceName = sourceName;
        this.warning = warning;
    }

    public string SourceName { get; }

    public Task<SourceFetchResult> FetchAsync(JobSearchCriteria criteria, CancellationToken cancellationToken) =>
        Task.FromResult(new SourceFetchResult(SourceName, [], [warning]));
}
