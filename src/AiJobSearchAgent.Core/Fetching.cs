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

public sealed class SimpleJobDescriptionFetcher : IJobDescriptionFetcher
{
    private readonly HttpClient httpClient;

    public SimpleJobDescriptionFetcher(HttpClient httpClient)
    {
        this.httpClient = httpClient;
    }

    public async Task<string> FetchFullDescriptionAsync(Uri url, CancellationToken cancellationToken)
    {
        try
        {
            // Simple fetch - in a real scenario, this would need to handle specific site parsing
            // or use a generic extraction service/LLM.
            var html = await httpClient.GetStringAsync(url, cancellationToken);
            
            // For MVP, we just return a message that we fetched it, 
            // but real extraction logic would go here (e.g. using AngleSharp).
            return $"[Fetched from {url}] Full description content would be extracted here.";
        }
        catch (Exception ex)
        {
            return $"Error fetching description: {ex.Message}";
        }
    }
}
