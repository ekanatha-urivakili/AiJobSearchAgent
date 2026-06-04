# Railway Deployment

## Services

Create two Railway services:

1. `ai-job-search-agent-worker`
2. `PostgreSQL`

The worker uses the project `Dockerfile`. PostgreSQL should use Railway's managed PostgreSQL plugin.

The default Railway deployment runs the scheduled worker. The local React dashboard, HTTP settings API, and CV upload screen require separate web services if you want them hosted on Railway.

## Environment Variables

Set these on the worker service:

```text
DATABASE_URL=${{Postgres.DATABASE_URL}}
SETTINGS_ENCRYPTION_KEY=<32-byte base64 key>
JOB_SEARCH_TIME_ZONE=Europe/London
JOB_SEARCH_RUN_AT=10:00
JOB_SEARCH_POSTCODE=MK4 4QG
JOB_SEARCH_RADIUS_MILES=50
JOB_SEARCH_POSTED_WITHIN_DAYS=7
JOB_SEARCH_MIN_PERMANENT_SALARY_GBP=75000
JOB_SEARCH_MIN_CONTRACT_DAY_RATE_GBP=400
JOB_SEARCH_MIN_CONTRACT_MONTHS=6
REED_API_KEY=<redacted>
SLACK_WEBHOOK_URL=<redacted>
GMAIL_CREDENTIALS_JSON=<redacted>
GMAIL_USER_EMAIL=you@your-domain.com
GMAIL_SEARCH_QUERY=label:job-alerts is:unread
INDEED_GMAIL_SEARCH_QUERY=from:jobalerts-noreply@indeed.com is:unread
```

Generate `SETTINGS_ENCRYPTION_KEY` with:

```bash
openssl rand -base64 32
```

Keep the value stable. Changing it prevents decrypting any previously saved secret values in `app_settings`.

For Gmail/Indeed alerts, `GMAIL_USER_EMAIL` must be the mailbox that receives job alerts. Service-account Gmail access also requires Google Workspace domain-wide delegation for the Gmail readonly scope.

## Database Schema

Apply the schema in `sql/init/001_schema.sql` to the Railway PostgreSQL database before enabling the scheduled worker.

For local Docker, the schema is applied automatically by the PostgreSQL container on first startup.

## Deploy Flow

1. Push the repository to GitHub.
2. Create a Railway project from the GitHub repo.
3. Add a Railway PostgreSQL service.
4. Add the environment variables above to the worker service.
5. Deploy the worker service.
6. Confirm logs show the next scheduled run at 10:00 Europe/London.

## Pre-Deploy Checks

Run these locally before pushing:

```bash
dotnet build
npm --prefix frontend run build
dotnet run --project tests/AiJobSearchAgent.Tests/AiJobSearchAgent.Tests.csproj
docker build -t ai-job-search-agent-railway-check .
docker run --rm ai-job-search-agent-railway-check
```

The container smoke test exits after one run unless Railway starts it with `--schedule` from `railway.toml`.

## Local Docker Flow

Start PostgreSQL only:

```bash
docker compose up -d postgres
```

Run the worker once:

```bash
dotnet run --project src/AiJobSearchAgent.Worker/AiJobSearchAgent.Worker.csproj
```

Run PostgreSQL and the worker container:

```bash
docker compose --profile worker up --build
```

## Local Schema Check

You can test PostgreSQL schema initialization locally with:

```bash
docker compose up -d postgres
psql "postgres://ai_job_search_agent:change-me-local-only@localhost:5432/ai_job_search_agent" -c "\dt"
```

The scheduled worker is the service deployed by the project `Dockerfile`. The HTTP settings API, CV upload API, and React dashboard are local developer tooling unless you add a separate Railway web service for `AiJobSearchAgent.McpServer` and a frontend service.

If you deploy the HTTP API to Railway, persist uploaded CVs with a Railway volume mounted to the app's `CVs/` path. Without a volume, uploaded CV files are container-local and may be lost on redeploy.

## Post-Deployment Evidence To Share

After deploying to Railway, share:

- Railway project and worker service names.
- Worker environment variable names with secret values redacted.
- Confirmation that `sql/init/001_schema.sql` was applied to Railway PostgreSQL.
- Startup logs showing `DATABASE_URL` connectivity and either the next scheduled run or one completed run.
- Latest `search_runs` rows:

```sql
SELECT status, raw_jobs_fetched, jobs_matched, started_at, completed_at
FROM search_runs
ORDER BY started_at DESC
LIMIT 5;
```

- Current source policy rows:

```sql
SELECT source_name, fetch_mode, enabled, minimum_delay_seconds, last_reviewed_on
FROM source_policies
ORDER BY source_name;
```
