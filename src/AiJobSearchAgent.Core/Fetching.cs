namespace AiJobSearchAgent.Core;

public interface IJobDescriptionFetcher
{
    Task<string> FetchFullDescriptionAsync(Uri url, CancellationToken cancellationToken);
}

public sealed class EmptyJobDescriptionFetcher : IJobDescriptionFetcher
{
    public Task<string> FetchFullDescriptionAsync(Uri url, CancellationToken cancellationToken) =>
        Task.FromResult(string.Empty);
}
