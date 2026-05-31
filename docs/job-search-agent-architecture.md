# AI Job Search Agent Architecture

## Current Repository Reality

This checkout currently contains infrastructure and data-model scaffolding only:

- `README.md`
- `docker-compose.yml`
- `railway.toml`
- `.env.example`
- `sql/init/001_schema.sql`
- `docs/railway-deployment.md`

The README references a .NET worker, tests, solution file, and Dockerfile, but those files are not present in this repository snapshot. The diagrams below document the intended project structure and runtime flow implied by the existing schema, environment variables, and deployment notes.

## High-Level Design

```mermaid
flowchart LR
    Scheduler["Daily scheduler\n10:00 Europe/London"] --> Worker["Job Search Worker"]
    Worker --> PolicyGuard["Source policy guard"]
    PolicyGuard --> Reed["Reed alert inbox adapter"]
    PolicyGuard --> JobServe["JobServe alert inbox adapter"]
    PolicyGuard -. disabled .-> Indeed["Indeed adapter\nDisabled until approved"]
    Reed --> Normalizer["Job normalizer"]
    JobServe --> Normalizer
    Normalizer --> Filter["Search criteria filter"]
    Filter --> Scorer["CV keyword scorer"]
    Scorer --> Store["PostgreSQL"]
    Store --> Report["Markdown report"]
    Report --> User["Senior fullstack developer"]
```

## Deployment View

```mermaid
flowchart TB
    subgraph Local["Local"]
        LocalWorker["dotnet worker or worker container"]
        LocalPg["Docker PostgreSQL"]
        LocalReports["./reports"]
        LocalWorker --> LocalPg
        LocalWorker --> LocalReports
    end

    subgraph Railway["Railway"]
        RailwayWorker["Worker service\nDockerfile start command"]
        RailwayPg["Managed PostgreSQL"]
        RailwayLogs["Railway logs"]
        RailwayWorker --> RailwayPg
        RailwayWorker --> RailwayLogs
    end
```

## Logical Low-Level Design

```mermaid
flowchart TD
    App["Worker entrypoint"] --> Config["Configuration loader"]
    Config --> SearchCriteria["Search criteria"]
    Config --> ScheduleOptions["Schedule options"]
    Config --> DatabaseOptions["Database options"]
    App --> Runner["Search run orchestrator"]
    Runner --> SourcePolicyRepository["Source policy repository"]
    Runner --> SourceAdapters["Source adapters"]
    SourceAdapters --> RawJobs["Raw source jobs"]
    RawJobs --> JobMapper["Job mapper"]
    JobMapper --> JobPosting["JobPosting model"]
    JobPosting --> CriteriaEvaluator["Criteria evaluator"]
    CriteriaEvaluator --> MatchCandidate["Match candidate"]
    MatchCandidate --> CvScorer["CV keyword scorer"]
    CvScorer --> MatchResult["Match result"]
    MatchResult --> Persistence["PostgreSQL persistence"]
    Persistence --> Reporter["Markdown report writer"]
```

### Core Components

| Component | Responsibility | Backing Evidence |
|---|---|---|
| Worker entrypoint | Run once or continuously on a schedule. | `README.md:22`, `README.md:28`, `railway.toml:6` |
| Configuration loader | Read search, schedule, and database settings. | `.env.example:1` |
| Source policy guard | Enforce allowed/disabled source behavior and delays. | `sql/init/001_schema.sql:58` |
| Source adapters | Fetch source-specific job alerts. | `README.md:7` |
| Criteria evaluator | Apply location, date, salary, rate, duration, title, and work-mode filters. | `README.md:14` |
| CV scorer | Score filtered jobs against the CV profile. | `README.md:9` |
| Persistence | Store runs, fetches, postings, matches, and seen history. | `sql/init/001_schema.sql:1` |
| Reporter | Produce a human-readable Markdown report. | `README.md:10` |

## Data Model

```mermaid
erDiagram
    search_runs ||--o{ source_fetches : records
    search_runs ||--o{ job_matches : creates
    job_postings ||--o{ job_matches : matched_as
    job_postings ||--o{ job_seen_history : has
    source_policies ||--o{ source_fetches : governs

    search_runs {
        uuid id PK
        timestamptz started_at
        timestamptz completed_at
        text status
        int raw_jobs_fetched
        int jobs_matched
    }

    source_fetches {
        uuid id PK
        uuid search_run_id FK
        text source_name
        text fetch_mode
        text status
        int jobs_fetched
        text warning
        text error
    }

    job_postings {
        uuid id PK
        text source
        text source_job_id
        text url
        text title
        text company
        text location
        numeric distance_miles
        text employment_type
        text work_mode
        numeric salary_min
        numeric salary_max
        numeric day_rate_min
        numeric day_rate_max
        int contract_months
        date posted_date
        text content_hash
    }

    job_matches {
        uuid id PK
        uuid search_run_id FK
        uuid job_posting_id FK
        int score
        boolean recommended
        jsonb reasons
        jsonb risks
    }

    job_seen_history {
        uuid id PK
        uuid job_posting_id FK
        text content_hash
        timestamptz reported_at
    }

    source_policies {
        text source_name PK
        text fetch_mode
        boolean enabled
        int minimum_delay_seconds
        date last_reviewed_on
    }
```

## Run Sequence

```mermaid
sequenceDiagram
    autonumber
    participant User
    participant Worker
    participant DB as PostgreSQL
    participant Policy as Source Policy Guard
    participant Source as Job Source Adapter
    participant Scorer as Filter and CV Scorer
    participant Report as Markdown Reporter

    User->>Worker: Start once or start scheduled mode
    Worker->>DB: Insert search_runs row
    Worker->>DB: Load source_policies
    Worker->>Policy: Check enabled source and fetch mode
    alt Source enabled
        Policy->>Source: Fetch alert jobs
        Source-->>Worker: Raw jobs
        Worker->>DB: Upsert job_postings
        Worker->>Scorer: Apply criteria and CV scoring
        Scorer-->>Worker: Match results
        Worker->>DB: Insert job_matches and job_seen_history
        Worker->>Report: Generate report
        Report-->>User: Markdown report
    else Source disabled
        Policy-->>Worker: Skip source with warning
        Worker->>DB: Insert source_fetches warning
    end
    Worker->>DB: Complete search_runs row
```

## Job Evaluation Flow

```mermaid
flowchart TD
    Start["Raw job"] --> PostedDate{"Posted within\nconfigured days?"}
    PostedDate -- No --> RejectDate["Reject: stale posting"]
    PostedDate -- Yes --> Location{"Within radius\nof postcode?"}
    Location -- No --> RejectLocation["Reject: out of range"]
    Location -- Yes --> Title{"Title matches\nsenior target roles?"}
    Title -- No --> RejectTitle["Reject: weak title match"]
    Title -- Yes --> Employment{"Permanent or contract?"}
    Employment -- Permanent --> Salary{"Salary >= minimum?"}
    Employment -- Contract --> Rate{"Day rate >= minimum?"}
    Salary -- No --> RejectSalary["Reject: salary below threshold"]
    Rate -- No --> RejectRate["Reject: rate below threshold"]
    Rate -- Yes --> Duration{"Duration >= minimum months?"}
    Duration -- No --> RejectDuration["Reject: short contract"]
    Salary -- Yes --> Score["Score against CV keywords"]
    Duration -- Yes --> Score
    Score --> Recommend{"Recommended?"}
    Recommend -- Yes --> StoreRecommended["Store recommended match"]
    Recommend -- No --> StoreRisk["Store match with risks"]
```

## Railway Flow

```mermaid
sequenceDiagram
    autonumber
    participant Dev as Developer
    participant GitHub
    participant Railway
    participant Worker as Worker Service
    participant PG as Railway PostgreSQL

    Dev->>GitHub: Push repository
    Railway->>GitHub: Build from repository
    Dev->>Railway: Add PostgreSQL service
    Dev->>Railway: Configure worker environment variables
    Dev->>PG: Apply sql/init/001_schema.sql
    Railway->>Worker: Deploy Docker worker
    Worker->>PG: Connect using DATABASE_URL
    Worker-->>Railway: Log next scheduled run
```

## What To Provide After Railway Deployment

Provide these artifacts so another senior developer can verify the deployment quickly:

- Railway project name and worker service name.
- Worker service environment variable list with secret values redacted.
- PostgreSQL service name and confirmation that `sql/init/001_schema.sql` was applied.
- Latest worker deployment logs from startup through either the scheduled wait message or one completed run.
- A database snapshot query result:

```sql
SELECT status, raw_jobs_fetched, jobs_matched, started_at, completed_at
FROM search_runs
ORDER BY started_at DESC
LIMIT 5;
```

- Source policy state:

```sql
SELECT source_name, fetch_mode, enabled, minimum_delay_seconds, last_reviewed_on
FROM source_policies
ORDER BY source_name;
```

- Generated report file path or Railway volume/artifact location, if report persistence is configured.
- Confirmation of how job-source credentials or alert inbox access are configured, with secrets redacted.

## Local Testing Status

You can test PostgreSQL schema initialization locally now:

```bash
docker compose up -d postgres
```

Then inspect the initialized tables:

```bash
psql "postgres://ai_job_search_agent:change-me-local-only@localhost:5432/ai_job_search_agent" -c "\dt"
```

You cannot currently run the worker locally from this checkout because the referenced .NET source files, solution file, tests, and Dockerfile are absent. Specifically, these README commands require files that are not present:

- `src/AiJobSearchAgent.Worker/AiJobSearchAgent.Worker.csproj`
- `tests/AiJobSearchAgent.Tests/AiJobSearchAgent.Tests.csproj`
- `AiJobSearchAgent.slnx`
- `Dockerfile`

You also cannot see emails directly from existing crawled jobs yet from this checkout because there is no email or alert inbox adapter implementation present. The current schema supports storing crawled jobs and matches once a worker exists, but it does not itself fetch emails or crawl job boards.

## Minimum Implementation Backlog

1. Add the .NET worker project and solution referenced by `README.md`.
2. Add source adapters for Reed and JobServe alert inbox ingestion.
3. Add a source policy guard backed by `source_policies`.
4. Add persistence for `search_runs`, `source_fetches`, `job_postings`, `job_matches`, and `job_seen_history`.
5. Add Markdown report generation to `reports/`.
6. Add a `Dockerfile` that matches `railway.toml`.
7. Add smoke tests for configuration, criteria filtering, scoring, and schema connectivity.
