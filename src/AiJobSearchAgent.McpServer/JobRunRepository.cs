using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AiJobSearchAgent.Core;
using Npgsql;
using NpgsqlTypes;

namespace AiJobSearchAgent.McpServer;

public sealed class JobRunRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string? connectionString;

    public JobRunRepository(string? connectionString)
    {
        this.connectionString = NormaliseConnectionString(connectionString);
    }

    public bool IsAvailable => !string.IsNullOrWhiteSpace(connectionString);

    public async Task EnsureSchemaAsync(CancellationToken ct = default)
    {
        if (!IsAvailable) return;

        await using var conn = await OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("""
            CREATE TABLE IF NOT EXISTS search_runs (
                id UUID PRIMARY KEY,
                started_at TIMESTAMPTZ NOT NULL,
                completed_at TIMESTAMPTZ,
                status TEXT NOT NULL,
                raw_jobs_fetched INTEGER NOT NULL DEFAULT 0,
                jobs_matched INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS source_fetches (
                id UUID PRIMARY KEY,
                search_run_id UUID NOT NULL REFERENCES search_runs(id) ON DELETE CASCADE,
                source_name TEXT NOT NULL,
                fetch_mode TEXT NOT NULL,
                status TEXT NOT NULL,
                jobs_fetched INTEGER NOT NULL DEFAULT 0,
                warning TEXT,
                error TEXT,
                started_at TIMESTAMPTZ NOT NULL,
                completed_at TIMESTAMPTZ
            );

            CREATE TABLE IF NOT EXISTS job_postings (
                id UUID PRIMARY KEY,
                source TEXT NOT NULL,
                source_job_id TEXT NOT NULL,
                url TEXT NOT NULL,
                title TEXT NOT NULL,
                company TEXT NOT NULL,
                location TEXT NOT NULL,
                distance_miles NUMERIC(8, 2) NOT NULL,
                employment_type TEXT NOT NULL,
                work_mode TEXT NOT NULL,
                salary_min NUMERIC(12, 2),
                salary_max NUMERIC(12, 2),
                day_rate_min NUMERIC(12, 2),
                day_rate_max NUMERIC(12, 2),
                contract_months INTEGER,
                posted_date DATE NOT NULL,
                description TEXT NOT NULL,
                content_hash TEXT NOT NULL,
                first_seen_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                last_seen_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                UNIQUE (source, source_job_id)
            );

            CREATE TABLE IF NOT EXISTS job_matches (
                id UUID PRIMARY KEY,
                search_run_id UUID NOT NULL REFERENCES search_runs(id) ON DELETE CASCADE,
                job_posting_id UUID NOT NULL REFERENCES job_postings(id) ON DELETE CASCADE,
                score INTEGER NOT NULL,
                recommended BOOLEAN NOT NULL,
                reasons JSONB NOT NULL,
                risks JSONB NOT NULL,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );

            CREATE TABLE IF NOT EXISTS job_seen_history (
                id UUID PRIMARY KEY,
                job_posting_id UUID NOT NULL REFERENCES job_postings(id) ON DELETE CASCADE,
                content_hash TEXT NOT NULL,
                reported_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                UNIQUE (job_posting_id, content_hash)
            );

            CREATE INDEX IF NOT EXISTS ix_job_postings_last_seen_at ON job_postings (last_seen_at DESC);
            CREATE INDEX IF NOT EXISTS ix_job_matches_search_run_id ON job_matches (search_run_id);

            CREATE TABLE IF NOT EXISTS job_applications (
                source TEXT NOT NULL,
                source_job_id TEXT NOT NULL,
                status TEXT NOT NULL,
                notes TEXT NOT NULL DEFAULT '',
                updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
                PRIMARY KEY (source, source_job_id)
            );
            """, conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<JobApplicationDto?> GetApplicationAsync(string source, string sourceJobId, CancellationToken ct = default)
    {
        if (!IsAvailable) return null;
        await EnsureSchemaAsync(ct);
        await using var conn = await OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("""
            SELECT source, source_job_id, status, notes, updated_at
            FROM job_applications
            WHERE source = @source AND source_job_id = @sourceJobId
            """, conn);
        cmd.Parameters.AddWithValue("source", source);
        cmd.Parameters.AddWithValue("sourceJobId", sourceJobId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return new(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetFieldValue<DateTimeOffset>(4));
    }

    public async Task<JobApplicationDto> SaveApplicationAsync(
        string source,
        string sourceJobId,
        string status,
        string? notes,
        CancellationToken ct = default)
    {
        if (!IsAvailable)
            return new(source, sourceJobId, status, notes ?? string.Empty, DateTimeOffset.UtcNow);

        await EnsureSchemaAsync(ct);
        await using var conn = await OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO job_applications (source, source_job_id, status, notes, updated_at)
            VALUES (@source, @sourceJobId, @status, @notes, now())
            ON CONFLICT (source, source_job_id) DO UPDATE
            SET status = EXCLUDED.status,
                notes = EXCLUDED.notes,
                updated_at = now()
            RETURNING source, source_job_id, status, notes, updated_at
            """, conn);
        cmd.Parameters.AddWithValue("source", source);
        cmd.Parameters.AddWithValue("sourceJobId", sourceJobId);
        cmd.Parameters.AddWithValue("status", status);
        cmd.Parameters.AddWithValue("notes", notes ?? string.Empty);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            throw new InvalidOperationException("Application save did not return a row.");
        return new(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetFieldValue<DateTimeOffset>(4));
    }

    public async Task SaveRunAsync(Guid runId, SearchRunResult result, CancellationToken ct = default)
    {
        if (!IsAvailable) return;

        await EnsureSchemaAsync(ct);
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        await using (var cmd = new NpgsqlCommand("""
            INSERT INTO search_runs (id, started_at, completed_at, status, raw_jobs_fetched, jobs_matched)
            VALUES (@id, @startedAt, @completedAt, @status, @rawJobsFetched, @jobsMatched)
            ON CONFLICT (id) DO UPDATE
            SET completed_at = EXCLUDED.completed_at,
                status = EXCLUDED.status,
                raw_jobs_fetched = EXCLUDED.raw_jobs_fetched,
                jobs_matched = EXCLUDED.jobs_matched
            """, conn, tx))
        {
            cmd.Parameters.AddWithValue("id", runId);
            cmd.Parameters.AddWithValue("startedAt", result.StartedAt);
            cmd.Parameters.AddWithValue("completedAt", result.CompletedAt);
            cmd.Parameters.AddWithValue("status", "Succeeded");
            cmd.Parameters.AddWithValue("rawJobsFetched", result.SourceResults.Sum(source => source.Jobs.Count));
            cmd.Parameters.AddWithValue("jobsMatched", result.Matches.Count);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        foreach (var source in result.SourceResults)
        {
            await SaveSourceFetchAsync(conn, tx, runId, source, result.StartedAt, result.CompletedAt, ct);
        }

        foreach (var match in result.Matches)
        {
            var jobId = await UpsertJobAsync(conn, tx, match.Job, ct);
            await SaveMatchAsync(conn, tx, runId, jobId, match, ct);
        }

        await tx.CommitAsync(ct);
    }

    private static async Task SaveSourceFetchAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        Guid runId,
        SourceFetchResult source,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO source_fetches (id, search_run_id, source_name, fetch_mode, status, jobs_fetched, warning, error, started_at, completed_at)
            VALUES (@id, @searchRunId, @sourceName, @fetchMode, @status, @jobsFetched, @warning, NULL, @startedAt, @completedAt)
            """, conn, tx);
        cmd.Parameters.AddWithValue("id", Guid.CreateVersion7());
        cmd.Parameters.AddWithValue("searchRunId", runId);
        cmd.Parameters.AddWithValue("sourceName", source.SourceName);
        cmd.Parameters.AddWithValue("fetchMode", "Unknown");
        cmd.Parameters.AddWithValue("status", source.Warnings.Count == 0 ? "Succeeded" : "Warning");
        cmd.Parameters.AddWithValue("jobsFetched", source.Jobs.Count);
        cmd.Parameters.AddWithValue("warning", source.Warnings.Count == 0 ? DBNull.Value : string.Join("; ", source.Warnings));
        cmd.Parameters.AddWithValue("startedAt", startedAt);
        cmd.Parameters.AddWithValue("completedAt", completedAt);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<Guid> UpsertJobAsync(NpgsqlConnection conn, NpgsqlTransaction tx, JobPosting job, CancellationToken ct)
    {
        var contentHash = ComputeContentHash(job);
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO job_postings (
                id, source, source_job_id, url, title, company, location, distance_miles,
                employment_type, work_mode, salary_min, salary_max, day_rate_min, day_rate_max,
                contract_months, posted_date, description, content_hash, first_seen_at, last_seen_at)
            VALUES (
                @id, @source, @sourceJobId, @url, @title, @company, @location, @distanceMiles,
                @employmentType, @workMode, @salaryMin, @salaryMax, @dayRateMin, @dayRateMax,
                @contractMonths, @postedDate, @description, @contentHash, now(), now())
            ON CONFLICT (source, source_job_id) DO UPDATE
            SET url = EXCLUDED.url,
                title = EXCLUDED.title,
                company = EXCLUDED.company,
                location = EXCLUDED.location,
                distance_miles = EXCLUDED.distance_miles,
                employment_type = EXCLUDED.employment_type,
                work_mode = EXCLUDED.work_mode,
                salary_min = EXCLUDED.salary_min,
                salary_max = EXCLUDED.salary_max,
                day_rate_min = EXCLUDED.day_rate_min,
                day_rate_max = EXCLUDED.day_rate_max,
                contract_months = EXCLUDED.contract_months,
                posted_date = EXCLUDED.posted_date,
                description = EXCLUDED.description,
                content_hash = EXCLUDED.content_hash,
                last_seen_at = now()
            RETURNING id
            """, conn, tx);

        cmd.Parameters.AddWithValue("id", Guid.CreateVersion7());
        cmd.Parameters.AddWithValue("source", job.Source);
        cmd.Parameters.AddWithValue("sourceJobId", job.SourceJobId);
        cmd.Parameters.AddWithValue("url", job.Url.ToString());
        cmd.Parameters.AddWithValue("title", job.Title);
        cmd.Parameters.AddWithValue("company", job.Company);
        cmd.Parameters.AddWithValue("location", job.Location);
        cmd.Parameters.AddWithValue("distanceMiles", job.DistanceMiles);
        cmd.Parameters.AddWithValue("employmentType", job.EmploymentType.ToString());
        cmd.Parameters.AddWithValue("workMode", job.WorkMode.ToString());
        AddNullable(cmd, "salaryMin", job.SalaryMin);
        AddNullable(cmd, "salaryMax", job.SalaryMax);
        AddNullable(cmd, "dayRateMin", job.DayRateMin);
        AddNullable(cmd, "dayRateMax", job.DayRateMax);
        AddNullable(cmd, "contractMonths", job.ContractMonths);
        cmd.Parameters.AddWithValue("postedDate", job.PostedDate);
        cmd.Parameters.AddWithValue("description", job.Description);
        cmd.Parameters.AddWithValue("contentHash", contentHash);

        var jobId = (Guid)(await cmd.ExecuteScalarAsync(ct) ?? throw new InvalidOperationException("Job upsert did not return an id."));
        await SaveSeenHistoryAsync(conn, tx, jobId, contentHash, ct);
        return jobId;
    }

    private static async Task SaveSeenHistoryAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        Guid jobId,
        string contentHash,
        CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO job_seen_history (id, job_posting_id, content_hash, reported_at)
            VALUES (@id, @jobPostingId, @contentHash, now())
            ON CONFLICT (job_posting_id, content_hash) DO NOTHING
            """, conn, tx);
        cmd.Parameters.AddWithValue("id", Guid.CreateVersion7());
        cmd.Parameters.AddWithValue("jobPostingId", jobId);
        cmd.Parameters.AddWithValue("contentHash", contentHash);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task SaveMatchAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        Guid runId,
        Guid jobId,
        JobMatch match,
        CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO job_matches (id, search_run_id, job_posting_id, score, recommended, reasons, risks)
            VALUES (@id, @searchRunId, @jobPostingId, @score, @recommended, @reasons, @risks)
            """, conn, tx);
        cmd.Parameters.AddWithValue("id", Guid.CreateVersion7());
        cmd.Parameters.AddWithValue("searchRunId", runId);
        cmd.Parameters.AddWithValue("jobPostingId", jobId);
        cmd.Parameters.AddWithValue("score", match.Score);
        cmd.Parameters.AddWithValue("recommended", match.Recommended);
        cmd.Parameters.Add("reasons", NpgsqlDbType.Jsonb).Value = JsonSerializer.Serialize(match.Reasons, JsonOptions);
        cmd.Parameters.Add("risks", NpgsqlDbType.Jsonb).Value = JsonSerializer.Serialize(match.Risks, JsonOptions);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
    {
        var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);
        return conn;
    }

    private static void AddNullable<T>(NpgsqlCommand cmd, string name, T? value)
        where T : struct
    {
        cmd.Parameters.AddWithValue(name, value.HasValue ? value.Value : DBNull.Value);
    }

    private static string ComputeContentHash(JobPosting job)
    {
        var value = $"{job.Source}|{job.SourceJobId}|{job.Title}|{job.Company}|{job.Location}|{job.Description}|{job.SalaryMin}|{job.SalaryMax}|{job.DayRateMin}|{job.DayRateMax}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash);
    }

    private static string? NormaliseConnectionString(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (!raw.TrimStart().StartsWith("postgres", StringComparison.OrdinalIgnoreCase) || raw.Contains(';'))
            return raw;

        try
        {
            var builder = new NpgsqlConnectionStringBuilder();
            var uri = new Uri(raw.Replace("postgres://", "postgresql://"));
            builder.Host = uri.Host;
            builder.Port = uri.Port > 0 ? uri.Port : 5432;
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
            return raw;
        }
    }
}
