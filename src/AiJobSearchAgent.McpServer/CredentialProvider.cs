namespace AiJobSearchAgent.McpServer;

public sealed class CredentialProvider
{
    private readonly Dictionary<string, string> fileVars;

    public CredentialProvider()
    {
        fileVars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var envPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".env");
            if (!File.Exists(envPath))
            {
                // also try repo root relative to current directory
                envPath = Path.Combine(Directory.GetCurrentDirectory(), ".env");
            }

            if (File.Exists(envPath))
            {
                foreach (var line in File.ReadAllLines(envPath))
                {
                    var trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#")) continue;
                    var idx = trimmed.IndexOf('=');
                    if (idx <= 0) continue;
                    var key = trimmed[..idx].Trim();
                    var raw = trimmed[(idx + 1)..].Trim();
                    // Strip a single wrapping quote pair — supports both "value" and 'value' (bash-sourced .env)
                    var value = raw.Length >= 2 && ((raw[0] == '"' && raw[^1] == '"') || (raw[0] == '\'' && raw[^1] == '\''))
                        ? raw[1..^1]
                        : raw;
                    fileVars[key] = value;
                }
            }
        }
        catch
        {
            // ignore any file read errors
        }
    }

    public string? GetSecret(string name)
    {
        var env = Environment.GetEnvironmentVariable(name);
        if (!string.IsNullOrWhiteSpace(env)) return env;
        return fileVars.TryGetValue(name, out var v) ? v : null;
    }
}
