namespace AiJobSearchAgent.McpServer;

public sealed class CredentialProvider
{
    public string? GetSecret(string name) => Environment.GetEnvironmentVariable(name);
}
