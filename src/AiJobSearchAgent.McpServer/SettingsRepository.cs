using System.Security.Cryptography;
using System.Text;
using Npgsql;

namespace AiJobSearchAgent.McpServer;

/// <summary>
/// Stores application settings in PostgreSQL with AES-256-GCM encryption for secrets.
/// Gracefully degrades to no-op when DATABASE_URL is not configured.
/// </summary>
public sealed class SettingsRepository
{
    public static readonly IReadOnlyDictionary<string, string> DefaultValues =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["JOB_SEARCH_TIME_ZONE"] = "Europe/London",
            ["JOB_SEARCH_RUN_AT"] = "10:00",
            ["JOB_SEARCH_POSTCODE"] = "MK4 4QG",
            ["JOB_SEARCH_RADIUS_MILES"] = "50",
            ["JOB_SEARCH_POSTED_WITHIN_DAYS"] = "7",
            ["JOB_SEARCH_MIN_PERMANENT_SALARY_GBP"] = "75000",
            ["JOB_SEARCH_MIN_CONTRACT_DAY_RATE_GBP"] = "400",
            ["JOB_SEARCH_MIN_CONTRACT_MONTHS"] = "6",
            ["JOB_SEARCH_EXCLUDED_KEYWORDS"] = "graduate,junior,java only,onsite 5 days,5 days onsite,sc clearance",
            ["JOB_SEARCH_DESIRED_DESIGNATION"] = "Senior Software Engineer,Lead Developer,Principal Engineer,Senior Fullstack Engineer,Senior Software Developer,Lead Software Engineer,Principal Developer",
            ["JOB_SEARCH_SKILLS"] = "c#,asp.net core,web api,react,typescript,javascript,php,aws,docker,sql server,postgresql,mysql,mongodb,microservices,cqrs,rest",
            ["REED_API_KEY"] = string.Empty,
        };

    public static readonly IReadOnlySet<string> ConfigurableKeys =
        new HashSet<string>(DefaultValues.Keys, StringComparer.OrdinalIgnoreCase)
        {
            "SLACK_WEBHOOK_URL",
            "GMAIL_CREDENTIALS_JSON",
            "GMAIL_USER_EMAIL",
            "GMAIL_SEARCH_QUERY",
            "INDEED_GMAIL_SEARCH_QUERY",
            "JOB_SEARCH_DESIRED_DESIGNATION",
            "JOB_SEARCH_SKILLS",
        };

    // Keys whose values are encrypted at rest in the database.
    public static readonly IReadOnlySet<string> SecretKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "REED_API_KEY",
        "GMAIL_CREDENTIALS_JSON",
        "SLACK_WEBHOOK_URL",
        "INDEED_GMAIL_SEARCH_QUERY",
        "GMAIL_SEARCH_QUERY",
    };

    private readonly string? connectionString;
    private readonly byte[]? encryptionKey;

    public SettingsRepository(string? connectionString, string? encryptionKeyBase64)
    {
        this.connectionString = NormaliseConnectionString(connectionString);

        if (!string.IsNullOrWhiteSpace(encryptionKeyBase64))
        {
            try
            {
                var raw = Convert.FromBase64String(encryptionKeyBase64);
                if (raw.Length != 32)
                    throw new InvalidOperationException("SETTINGS_ENCRYPTION_KEY must decode to exactly 32 bytes.");
                encryptionKey = raw;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[settings] SETTINGS_ENCRYPTION_KEY is invalid: {ex.Message}");
            }
        }
    }

    public bool IsAvailable => !string.IsNullOrWhiteSpace(connectionString);

    // ── schema ────────────────────────────────────────────────────────────────

    public async Task EnsureSchemaAsync(CancellationToken ct = default)
    {
        if (!IsAvailable) return;
        try
        {
            await using var conn = await OpenAsync(ct);
            await using var cmd = new NpgsqlCommand("""
                CREATE TABLE IF NOT EXISTS app_settings (
                    key        text        PRIMARY KEY,
                    value      text        NOT NULL,
                    is_secret  boolean     NOT NULL DEFAULT false,
                    updated_at timestamptz NOT NULL DEFAULT now()
                )
                """, conn);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[settings] Schema migration failed: {ex.Message}");
        }
    }

    // ── read ──────────────────────────────────────────────────────────────────

    /// <summary>Returns all settings, decrypting secrets. Returns empty dict on DB error.</summary>
    public async Task<Dictionary<string, string>> GetAllAsync(CancellationToken ct = default)
    {
        if (!IsAvailable) return new();
        try
        {
            await using var conn = await OpenAsync(ct);
            await using var cmd = new NpgsqlCommand(
                "SELECT key, value, is_secret FROM app_settings ORDER BY key", conn);
            await using var reader = await cmd.ExecuteReaderAsync(ct);

            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            while (await reader.ReadAsync(ct))
            {
                var key      = reader.GetString(0);
                var rawValue = reader.GetString(1);
                var isSecret = reader.GetBoolean(2);
                result[key]  = isSecret ? TryDecrypt(rawValue) : rawValue;
            }
            return result;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[settings] Read failed: {ex.Message}");
            return new();
        }
    }

    // ── write ─────────────────────────────────────────────────────────────────

    /// <summary>Upserts each key, encrypting secrets. Skips blank secret values.</summary>
    public async Task SaveAsync(IReadOnlyDictionary<string, string> settings, CancellationToken ct = default)
    {
        if (!IsAvailable) return;
        try
        {
            await using var conn = await OpenAsync(ct);
            foreach (var (key, value) in settings)
            {
                if (!ConfigurableKeys.Contains(key)) continue;
                if (string.IsNullOrEmpty(value) && SecretKeys.Contains(key)) continue;

                var isSecret    = SecretKeys.Contains(key);
                var storedValue = isSecret ? Encrypt(value) : value;

                await using var cmd = new NpgsqlCommand("""
                    INSERT INTO app_settings (key, value, is_secret, updated_at)
                    VALUES (@key, @value, @isSecret, now())
                    ON CONFLICT (key) DO UPDATE
                    SET value = EXCLUDED.value,
                        is_secret = EXCLUDED.is_secret,
                        updated_at = now()
                    """, conn);
                cmd.Parameters.AddWithValue("key",      key);
                cmd.Parameters.AddWithValue("value",    storedValue);
                cmd.Parameters.AddWithValue("isSecret", isSecret);
                await cmd.ExecuteNonQueryAsync(ct);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[settings] Write failed: {ex.Message}");
            throw; // Surface to the caller so the HTTP endpoint returns 500
        }
    }

    // ── encryption ────────────────────────────────────────────────────────────
    // Format: "ENC:" + Base64( nonce[12] || ciphertext || tag[16] )
    // Secret writes require SETTINGS_ENCRYPTION_KEY so new secrets are never stored in plaintext.

    private string Encrypt(string plaintext)
    {
        if (encryptionKey is null)
            throw new InvalidOperationException("SETTINGS_ENCRYPTION_KEY is required before saving secret settings.");

        var nonce       = new byte[AesGcm.NonceByteSizes.MaxSize]; // 12 bytes
        RandomNumberGenerator.Fill(nonce);

        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext     = new byte[plaintextBytes.Length];
        var tag            = new byte[AesGcm.TagByteSizes.MaxSize]; // 16 bytes

        using var aes = new AesGcm(encryptionKey, tag.Length);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        var combined = new byte[nonce.Length + ciphertext.Length + tag.Length];
        nonce.CopyTo(combined, 0);
        ciphertext.CopyTo(combined, nonce.Length);
        tag.CopyTo(combined, nonce.Length + ciphertext.Length);

        return "ENC:" + Convert.ToBase64String(combined);
    }

    private string TryDecrypt(string stored)
    {
        if (stored.StartsWith("PLAIN:", StringComparison.Ordinal))
            return stored[6..];

        if (!stored.StartsWith("ENC:", StringComparison.Ordinal))
            return stored; // Not encrypted (legacy plain value)

        if (encryptionKey is null)
        {
            Console.Error.WriteLine("[settings] Cannot decrypt: SETTINGS_ENCRYPTION_KEY is not set.");
            return string.Empty;
        }

        try
        {
            var combined   = Convert.FromBase64String(stored[4..]);
            var nonceSize  = AesGcm.NonceByteSizes.MaxSize;  // 12
            var tagSize    = AesGcm.TagByteSizes.MaxSize;     // 16

            var nonce      = combined[..nonceSize];
            var ciphertext = combined[nonceSize..^tagSize];
            var tag        = combined[^tagSize..];

            var plaintext  = new byte[ciphertext.Length];
            using var aes  = new AesGcm(encryptionKey, tagSize);
            aes.Decrypt(nonce, ciphertext, tag, plaintext);

            return Encoding.UTF8.GetString(plaintext);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[settings] Decryption failed for stored value: {ex.Message}");
            return string.Empty;
        }
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
    {
        var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);
        return conn;
    }

    /// <summary>Converts postgres:// URI format to Npgsql connection string if needed.</summary>
    private static string? NormaliseConnectionString(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        // Already a key=value connection string
        if (!raw.TrimStart().StartsWith("postgres", StringComparison.OrdinalIgnoreCase) || raw.Contains(';'))
            return raw;

        try
        {
            // postgres://user:password@host:port/db
            var builder = new NpgsqlConnectionStringBuilder();
            var uri     = new Uri(raw.Replace("postgres://", "postgresql://"));
            builder.Host     = uri.Host;
            builder.Port     = uri.Port > 0 ? uri.Port : 5432;
            builder.Database = uri.AbsolutePath.TrimStart('/');
            if (!string.IsNullOrEmpty(uri.UserInfo))
            {
                var parts = uri.UserInfo.Split(':', 2);
                builder.Username = Uri.UnescapeDataString(parts[0]);
                if (parts.Length == 2) builder.Password = Uri.UnescapeDataString(parts[1]);
            }
            return builder.ConnectionString;
        }
        catch
        {
            return raw; // Return as-is and let Npgsql report the error
        }
    }
}
