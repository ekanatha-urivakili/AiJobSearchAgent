# Railway Deployment

## Services

Create two Railway services:

1. `ai-job-search-agent-worker`
2. `PostgreSQL`

The worker uses the project `Dockerfile`. PostgreSQL should use Railway's managed PostgreSQL plugin.

## Environment Variables

Set these on the worker service:

```text
DATABASE_URL=${{Postgres.DATABASE_URL}}
JOB_SEARCH_TIME_ZONE=Europe/London
JOB_SEARCH_RUN_AT=10:00
JOB_SEARCH_POSTCODE=MK4 4QG
JOB_SEARCH_RADIUS_MILES=50
JOB_SEARCH_POSTED_WITHIN_DAYS=7
JOB_SEARCH_MIN_PERMANENT_SALARY_GBP=75000
JOB_SEARCH_MIN_CONTRACT_DAY_RATE_GBP=400
JOB_SEARCH_MIN_CONTRACT_MONTHS=6
```

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
