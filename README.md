# AI Job Search Agent

Automated job search agent for senior UK software roles matched against Ekanatha Reddy Urivakili's CV profile.

## How It Works — Step by Step

This is what happens from first run to seeing matched jobs in your dashboard:

### Step 1 — Configure your profile and credentials

Open the **Settings** page in the dashboard (`http://localhost:5173/settings`) and fill in:

1. **Job Profile** — your desired designations (e.g. `Senior Software Engineer, Lead Developer`) and the skills you want matched against job listings (e.g. `C#, React, AWS`). These are saved as `JOB_SEARCH_DESIRED_DESIGNATION` and `JOB_SEARCH_SKILLS`.
2. **Search Criteria** — postcode, radius, posting age, salary/day-rate floors, contract minimums.
3. **Excluded Keywords** — noisy phrases rejected before scoring, such as `graduate`, `java only`, or `sc clearance`.
4. **Reed API Key** — enables live job fetching from Reed.co.uk.
5. **Gmail Integration** — service account JSON + mailbox email, so the agent can read job alert emails.
6. **Slack Webhook** — optional; posts top matches (score ≥ 85) to a channel.

Saved values are persisted to PostgreSQL (secrets encrypted with AES-256-GCM) and immediately applied as environment variables in the running server process.

### Step 2 — The server loads settings on startup

When `AiJobSearchAgent.McpServer --http` starts:

1. Connects to PostgreSQL and ensures the `app_settings` table exists.
2. Reads all stored settings and calls `Environment.SetEnvironmentVariable` for each key — so `Defaults.CreateCriteria()` and `Defaults.CreateCvProfile()` pick up your saved profile without a restart.
3. Starts the HTTP API on `localhost:5001` by default (and CORS-allows the Vite dev server). Remote callers must supply `JOB_AGENT_API_KEY`.

### Step 3 — A search run is triggered

A search run can be triggered three ways:

| Trigger | How |
|---|---|
| Dashboard load | `GET /api/jobs/results` — returns cached run if one exists, otherwise runs a fresh default search |
| Manual refresh | `GET /api/jobs/search` — always runs a fresh search and caches the result |
| Daily worker | `AiJobSearchAgent.Worker --schedule` fires at the configured time (default `10:00 Europe/London`) |

### Step 4 — Jobs are fetched from all enabled sources

`JobSearchOrchestrator.RunAsync` calls each enabled source adapter in parallel:

| Source | How it fetches | What enables it |
|---|---|---|
| **Reed** | Reed Job Search API (`/api/1.0/search`) | `REED_API_KEY` configured |
| **Gmail Alerts** | Reads unread emails via Google service account | `GMAIL_CREDENTIALS_JSON` + `GMAIL_USER_EMAIL` |
| **Indeed Direct** | In-memory buffer filled by `POST /api/jobs/ingest_indeed` | Buffer non-empty |
| **Dice / ZipRecruiter** | In-memory buffers via MCP tool calls | Buffer non-empty |

`SourcePolicyGuard` rate-limits and can disable sources by policy. Disabled or misconfigured sources return a `PolicyBlocked` status but do not fail the run.

### Step 5 — Each job is filtered

`JobFilterEngine` checks each posting against your settings in this order:

1. **Posted date** — must be within `JOB_SEARCH_POSTED_WITHIN_DAYS` (default 7).
2. **Distance** — must be within `JOB_SEARCH_RADIUS_MILES` of `JOB_SEARCH_POSTCODE` (remote jobs bypass this).
3. **Excluded keywords** — title and description must not contain `JOB_SEARCH_EXCLUDED_KEYWORDS`.
4. **Title match** — title must contain one of the configured designations from `JOB_SEARCH_DESIRED_DESIGNATION`.
5. **Salary / day rate** — permanent roles at or above `JOB_SEARCH_MIN_PERMANENT_SALARY_GBP`; contract roles at or above `JOB_SEARCH_MIN_CONTRACT_DAY_RATE_GBP` and `JOB_SEARCH_MIN_CONTRACT_MONTHS`. Roles without published pay are passed through.

Jobs failing any check are counted in the **Rejection Summary** visible on the dashboard.

### Step 6 — Passing jobs are scored against your CV

`CvMatchScorer` calculates a 0–100 score per job using keyword signals from your profile:

| Signal group | Source | Weight |
|---|---|---|
| Core skills | `JOB_SEARCH_SKILLS` | High |
| Domain keywords | Hardcoded (fintech, payments, e-commerce, …) | Medium |
| Leadership keywords | Hardcoded (lead, senior, mentor, …) | Medium |

Matching keywords become the **"Why it matches"** reasons shown on the job detail page. Missing keywords of note become **"Risks to review"**.

Scoring thresholds:

- **≥ 85** → Recommended + Slack alert
- **≥ 70** → Good match (shown in dashboard)
- **< 70** → Rejected (counted in summary)

### Step 7 — Results are deduplicated and cached

Jobs are deduplicated by `(source, sourceJobId)` to prevent duplicate postings per source. Search runs, matched postings, match reasons, and content hashes are persisted to PostgreSQL when `DATABASE_URL` is configured. The final `SearchRunResult` is also cached in memory in `JobSearchMcpService` and served instantly on subsequent `GET /api/jobs/results` calls until the next search run.

### Step 8 — Reports and alerts are sent

After scoring:

- **Markdown report** — written to `reports/daily-job-matches-YYYY-MM-DD.md`.
- **Slack alert** — each recommended job (score ≥ 85) is posted to the configured webhook with title, company, score, compensation, and a direct link.

### Step 9 — Browse and act

The React dashboard at `http://localhost:5173` shows:

- **Dashboard** — top picks, source status, rejection summary.
- **Browse Jobs** — filter by work mode, employment type, location, salary, and quality. Sort by score, date, or salary.
- **Job Detail** — full description, CV match reasons, risks, and a direct link to the original advert.
- **Pipeline Status** — mark each job as new, interested, applied, follow-up, interview, rejected, or offer with notes.

---

## What is built

- `.NET 10` worker with daily scheduler (Europe/London timezone)
- Reed API job source adapter (live)
- Gmail job alert source adapter — reads job alert emails via Google service account credentials (live)
- Indeed Direct MCP ingestion — accepts normalized jobs fetched by an external MCP plugin into an in-memory source buffer
- Source policy guard — per-source enable/disable and rate limiting
- Deterministic filter engine — salary, day rate, location, title, recency
- CV keyword scorer — core skills, domain, leadership signals
- Markdown report generator — writes daily file to `/reports`
- Slack reporter — posts strong matches (score ≥ 85) via incoming webhook
- Dual-mode MCP server — STDIO for AI clients, HTTP for the React dashboard
- React/Vite dashboard — filter, sort, paginate, and open job adverts
- Settings panel — configure search criteria, Reed, Slack, Gmail, and Indeed settings
- Secure settings store — PostgreSQL `app_settings` table with AES-GCM encryption for secrets
- CV upload — saves `.pdf`, `.docx`, and `.md` files to `CVs/` with replace-or-rename flow
- PostgreSQL schema, Docker Compose, Dockerfile, and Railway worker deployment

## Architecture

```mermaid
flowchart TD
    subgraph Clients
        WebUI["React Dashboard\n(Vite, port 5173)"]
        McpClient["MCP Client\n(Claude Desktop etc.)"]
    end

    subgraph McpServer["AiJobSearchAgent.McpServer"]
        HttpApi["HTTP API\n(--http flag, port 5001)"]
        StdioMcp["STDIO MCP Server\n(default mode)"]
        McpService["JobSearchMcpService"]
        ResultApi["Results API\nGET /api/jobs/results\nGET /api/jobs/search\nPOST /api/jobs/ingest_indeed"]
        ConfigApi["Config API\nGET/POST /api/config"]
        CvsApi["CV API\nGET /api/cvs\nPOST /api/cvs/upload"]
        SettingsRepo["SettingsRepository\nPostgreSQL + AES-GCM"]
        CredProvider["CredentialProvider\n(env vars + .env file)"]
    end

    subgraph Core["AiJobSearchAgent.Core"]
        Orchestrator["JobSearchOrchestrator"]
        PolicyGuard["SourcePolicyGuard"]
        FilterEngine["JobFilterEngine"]
        Scorer["CvMatchScorer"]
        Dedup["Deduplicator\n(source|sourceJobId)"]
    end

    subgraph Sources
        ReedAdapter["Reed API Adapter"]
        GmailAdapter["GmailAlertJobSourceAdapter\n(Google.Apis.Gmail.v1)"]
        IndeedDirect["IndeedDirectJobSourceAdapter\n(in-memory MCP ingest buffer)"]
    end

    subgraph Reporters
        MdReporter["MarkdownFileJobReporter\n(/reports/daily-*.md)"]
        SlackReporter["SlackJobReporter\n(score >= 85 only)"]
    end

    subgraph Worker["AiJobSearchAgent.Worker"]
        Scheduler["Daily Scheduler\n(10:00 Europe/London)"]
    end

    WebUI --> HttpApi
    McpClient --> StdioMcp
    WebUI --> ConfigApi
    WebUI --> CvsApi
    HttpApi --> ResultApi
    ResultApi --> McpService
    StdioMcp --> McpService
    McpService --> Orchestrator
    Scheduler --> Orchestrator
    ConfigApi --> CredProvider
    ConfigApi --> SettingsRepo
    CvsApi --> CvFolder[("CVs/")]

    Orchestrator --> PolicyGuard
    Orchestrator --> ReedAdapter
    Orchestrator --> GmailAdapter
    Orchestrator --> IndeedDirect
    Orchestrator --> FilterEngine
    FilterEngine --> Scorer
    Scorer --> Dedup
    Dedup --> MdReporter
    Dedup --> SlackReporter

    GmailAdapter --> Gmail[("Gmail API")]
    McpClient -- "jobs.ingest_indeed" --> StdioMcp
    ReedAdapter --> ReedAPI[("Reed API")]
    SlackReporter --> Slack[("Slack Webhook")]
    MdReporter --> Reports[("reports/")]
    SettingsRepo --> Postgres[("PostgreSQL")]
```

## Deployment

```mermaid
flowchart TB
    subgraph Local["Local Development"]
        DevUI["npm run dev\n(frontend, port 5173)"]
        DevMcp["dotnet run McpServer --http\n(port 5001)"]
        DevWorker["dotnet run Worker\n(one-shot or --schedule)"]
        LocalPg["Docker PostgreSQL\n(docker compose up postgres)"]
        DotEnv[".env file\n(credentials + config)"]
        LocalCvs["CVs/\nlocal uploads"]

        DevUI -- "GET /api/jobs/results" --> DevMcp
        DevUI -- "explicit refresh: GET /api/jobs/search" --> DevMcp
        DevUI -- "GET/POST /api/config" --> DevMcp
        DevUI -- "GET/POST /api/cvs" --> DevMcp
        DevMcp --> LocalPg
        DevWorker --> LocalPg
        DevMcp --> DotEnv
        DevWorker --> DotEnv
        DevMcp --> LocalCvs
    end

    subgraph Railway["Railway Production"]
        RailwayWorker["Worker --schedule\n(Dockerfile + railway.toml)"]
        RailwayPg["Managed PostgreSQL"]
        RailwaySecrets["Railway Env Vars\n(secrets)"]

        RailwayWorker --> RailwayPg
        RailwayWorker --> RailwaySecrets
    end

    subgraph OptionalRailwayWeb["Optional Railway Web UI"]
        RailwayMcp["McpServer --http"]
        RailwayFrontend["Frontend static/web service"]
        RailwayFrontend --> RailwayMcp
        RailwayMcp --> RailwayPg
        RailwayMcp --> RailwaySecrets
    end
```

## Job Evaluation Flow

```mermaid
flowchart TD
    Start["Raw job posting"] --> PostedDate{"Posted within\nconfigured days?"}
    PostedDate -- No --> RejectDate["Reject: stale"]
    PostedDate -- Yes --> Location{"Within radius\nof postcode?"}
    Location -- No --> RejectLocation["Reject: out of range"]
    Location -- Yes --> Title{"Title matches\ntarget roles?"}
    Title -- No --> RejectTitle["Reject: weak title"]
    Title -- Yes --> Employment{"Permanent or contract?"}
    Employment -- Permanent --> SalaryKnown{"Salary\npublished?"}
    Employment -- Contract --> Rate{"Day rate >= minimum?"}
    SalaryKnown -- Yes --> Salary{"Salary >= minimum?"}
    SalaryKnown -- No --> Score
    Salary -- No --> RejectSalary["Reject: below salary floor"]
    Salary -- Yes --> Score["Score against CV keywords\n(core skills, domain, leadership)"]
    Rate -- No --> RejectRate["Reject: below rate floor"]
    Rate -- Yes --> Duration{"Duration >= min months?"}
    Duration -- No --> RejectDuration["Reject: short contract"]
    Duration -- Yes --> Score
    Score --> Threshold{"Score >= 85?"}
    Threshold -- Yes --> Strong["Strong match → Slack alert"]
    Threshold -- No --> Threshold2{"Score >= 70?"}
    Threshold2 -- Yes --> Good["Good match → Markdown report"]
    Threshold2 -- No --> Below["Below threshold → rejected"]
```

## Run Sequence

```mermaid
sequenceDiagram
    autonumber
    participant Client as Web UI / MCP Client
    participant Server as McpServer
    participant Settings as SettingsRepository
    participant Ingest as Indeed Direct Buffer
    participant Orchestrator
    participant Reed as Reed API
    participant Gmail as Gmail API
    participant Scorer as Filter + Scorer

    opt MCP client has external Indeed plugin results
        Client->>Server: jobs.ingest_indeed
        Server->>Ingest: Replace or append normalized jobs
    end

    Client->>Server: Search (HTTP GET /api/jobs/search or MCP jobs.search)
    Server->>Settings: Load saved config (HTTP mode)
    Server->>Orchestrator: RunAsync(criteria, profile)

    Orchestrator->>Reed: FetchAsync
    Reed-->>Orchestrator: JobPostings[]

    Orchestrator->>Gmail: FetchAsync (service account credentials)
    Gmail-->>Orchestrator: JobPostings[] (parsed from alert emails)

    Orchestrator->>Ingest: FetchAsync (Indeed Direct)
    Ingest-->>Orchestrator: JobPostings[] (previously ingested)

    Orchestrator->>Scorer: Filter + score all jobs
    Scorer-->>Orchestrator: JobMatches[]

    Orchestrator->>Orchestrator: Deduplicate by source|sourceJobId
    Orchestrator-->>Server: SearchRunResult
    Server-->>Client: Matches (JSON)
```

`GET /api/jobs/results` returns the latest in-memory run when one exists. If no run has happened in the current HTTP server process, it performs a default search and caches that result. `GET /api/jobs/search` always triggers a fresh default search.

## Search Criteria

| Setting | Default | Env var |
|---|---|---|
| Desired designations | `Senior Software Engineer, Lead Developer, …` | `JOB_SEARCH_DESIRED_DESIGNATION` |
| Skills | `c#, asp.net core, react, typescript, aws, …` | `JOB_SEARCH_SKILLS` |
| Postcode | `MK4 4QG` | `JOB_SEARCH_POSTCODE` |
| Radius | 50 miles | `JOB_SEARCH_RADIUS_MILES` |
| Posted within | 7 days | `JOB_SEARCH_POSTED_WITHIN_DAYS` |
| Min permanent salary | £75,000/yr | `JOB_SEARCH_MIN_PERMANENT_SALARY_GBP` |
| Min contract day rate | £400/day | `JOB_SEARCH_MIN_CONTRACT_DAY_RATE_GBP` |
| Min contract duration | 6 months | `JOB_SEARCH_MIN_CONTRACT_MONTHS` |
| Excluded keywords | `graduate,junior,java only,onsite 5 days,5 days onsite,sc clearance` | `JOB_SEARCH_EXCLUDED_KEYWORDS` |
| Time zone | `Europe/London` | `JOB_SEARCH_TIME_ZONE` |
| Run time | `10:00` | `JOB_SEARCH_RUN_AT` |
| Gmail mailbox user | _(none)_ | `GMAIL_USER_EMAIL` |

`JOB_SEARCH_DESIRED_DESIGNATION` and `JOB_SEARCH_SKILLS` are configurable from the **Job Profile** section of the Settings page. Both accept comma-separated values and fall back to built-in defaults when not set.

## Configuration

Copy `.env.example` to `.env` and fill in the secrets. The HTTP server reads this file at startup via `CredentialProvider`. All values can also be set as real environment variables; real environment variables take priority over `.env`.

The Settings screen can persist configurable values to PostgreSQL through `POST /api/config`. Secret settings are encrypted in the `app_settings` table with AES-256-GCM and require `SETTINGS_ENCRYPTION_KEY`.

The HTTP API binds to `localhost:5001` by default. Set `JOB_AGENT_HTTP_HOST=0.0.0.0` only when exposing it through a trusted network boundary, and set `JOB_AGENT_API_KEY` so callers must send `X-Job-Agent-Key` or `Authorization: Bearer <key>`. The frontend can send the key with `VITE_JOB_AGENT_API_KEY`.

Generate a local encryption key with:

```bash
openssl rand -base64 32
```

Set it in `.env` or Railway variables:

```text
SETTINGS_ENCRYPTION_KEY=<generated-value>
```

Keep this value stable. Changing it prevents decrypting previously saved secrets.

### Reed API Key

Reed jobs use the approved Reed API. Set:

```text
REED_API_KEY=<redacted>
```

If the key is absent, Reed is shown as not ready/skipped and the rest of the run continues.

### Slack Webhook URL

A Slack incoming webhook URL. In your Slack workspace: **Settings → Integrations → Incoming Webhooks → Add New Webhook**.

```
SLACK_WEBHOOK_URL=https://hooks.slack.com/services/T00000000/B00000000/XXXXXXXXXXXXXXXXXXXXXXXX
```

The worker posts a message per run when any job scores ≥ 85. If the variable is absent or blank, Slack reporting is silently skipped.

### Gmail Credentials JSON

A Google **service account** credentials JSON file, pasted inline as a single value. The service account needs **Gmail API** access (`gmail.readonly` scope) and must be delegated to the mailbox configured in `GMAIL_USER_EMAIL`.

1. Go to Google Cloud Console → **IAM & Admin → Service Accounts → Create Service Account**.
2. Enable the **Gmail API** on the project.
3. Under the service account, create a **JSON key** and download it.
4. For Google Workspace, enable domain-wide delegation for the service account and authorize the Gmail readonly scope in Admin Console.
5. Set `GMAIL_USER_EMAIL` to the mailbox that receives job alerts.

The JSON looks like:

```json
{
  "type": "service_account",
  "project_id": "my-project-123",
  "private_key_id": "abc123...",
  "private_key": "-----BEGIN RSA PRIVATE KEY-----\n...\n-----END RSA PRIVATE KEY-----\n",
  "client_email": "my-agent@my-project-123.iam.gserviceaccount.com",
  "client_id": "123456789",
  "auth_uri": "https://accounts.google.com/o/oauth2/auth",
  "token_uri": "https://oauth2.googleapis.com/token"
}
```

Set it in `.env` as a single-line value (the UI settings panel handles the formatting):

```
GMAIL_CREDENTIALS_JSON={"type":"service_account","project_id":"my-project-123",...}
GMAIL_USER_EMAIL=you@your-domain.com
```

If either `GMAIL_CREDENTIALS_JSON` or `GMAIL_USER_EMAIL` is absent or blank, the Gmail source adapter returns an empty result with a warning — the rest of the run continues normally.

### Gmail Search Query

A standard Gmail search string, same syntax as the Gmail search box:

```
GMAIL_SEARCH_QUERY=label:job-alerts is:unread
```

Other examples:

```
from:noreply@reed.co.uk is:unread
subject:"job alert" newer_than:7d
label:jobs -label:applied
```

Default is `label:job-alerts is:unread`. Override this to narrow or broaden which emails are parsed for job postings.

### Indeed Direct MCP Ingestion

`jobs.ingest_indeed` is an MCP/HTTP ingestion path for jobs fetched by an external Indeed MCP plugin. The caller maps plugin results into `IndeedJobInput`; the server stores them in the `Indeed Direct` in-memory adapter. A later `jobs.search` can include `sources=["Indeed Direct"]`, or omit `sources` to include it with the other configured sources.

The buffer is process-local. Restarting `AiJobSearchAgent.McpServer` clears it, and `clearFirst=true` replaces the previous batch before adding the new jobs.

**Forking this project — Claude Pro not required.** The `POST /api/jobs/ingest_indeed` endpoint is a generic HTTP endpoint that accepts any `IndeedJobInput[]` payload. It has no dependency on Claude. Callers can feed it from the [Indeed Publisher API](https://ads.indeed.com/jobroll/xmlfeed), any other job board, or a custom scraper — Claude is only one possible way to obtain the input data. A Claude account with the Indeed MCP plugin is needed only if you want the Claude-mediated flow (Claude calls the plugin, maps results, then POSTs to this endpoint). The renaming of this adapter to something more generic (e.g. `ExternalJobIngestAdapter`) is a future cleanup task.

### Settings Panel

The React dashboard includes a **Settings** screen where you can configure:

- Search schedule, postcode, radius, posting age, salary floor, day-rate floor, minimum contract months, and excluded keywords.
- `REED_API_KEY`.
- Slack webhook URL.
- Gmail service-account JSON, delegated mailbox user, and Gmail search query.

`GET /api/config` returns saved values plus defaults, but secret values are masked in API responses. Blank secret fields preserve existing saved secrets.

### CV Uploads

The Settings screen also supports CV uploads. Files are saved under `CVs/` and git ignores uploaded CV documents by default.

Allowed extensions:

- `.pdf`
- `.docx`
- `.md`

When a CV already exists, the UI asks whether to replace an existing file or add the upload with a new filename. The backend also validates extensions, checks basic file signatures, enforces a 5 MB limit, and sanitizes filenames in `POST /api/cvs/upload`.

## Run Locally

### 1. Start PostgreSQL

```bash
docker compose up -d postgres
```

### 2. Start the HTTP server

```bash
dotnet run --project src/AiJobSearchAgent.McpServer/AiJobSearchAgent.McpServer.csproj -- --http
```

### 3. Start the dashboard

```bash
cd frontend
npm install
npm run dev
```

Open the Vite URL (default `http://localhost:5173`; Vite may fall back to `5174`). Use Settings to configure search criteria, integrations, and CV uploads.

### 4. Run the worker once

```bash
dotnet run --project src/AiJobSearchAgent.Worker/AiJobSearchAgent.Worker.csproj
```

### 5. Run the worker on schedule

```bash
dotnet run --project src/AiJobSearchAgent.Worker/AiJobSearchAgent.Worker.csproj -- --schedule
```

Waits until 10:00 Europe/London, runs, then repeats daily.

## MCP Mode (Claude Desktop)

Run without `--http` to expose job search tools over STDIO:

```bash
dotnet run --project src/AiJobSearchAgent.McpServer/AiJobSearchAgent.McpServer.csproj
```

Add to your Claude Desktop `claude_desktop_config.json` under `mcpServers`.

## Run Tests

```bash
dotnet run --project tests/AiJobSearchAgent.Tests/AiJobSearchAgent.Tests.csproj
```

## Build

```bash
dotnet build AiJobSearchAgent.slnx
```

## Docker Compose

```bash
# PostgreSQL only
docker compose up -d postgres

# PostgreSQL + worker
docker compose --profile worker up --build
```

## Docker Build

Railway uses the repository `Dockerfile`, which builds the scheduled worker image:

```bash
docker build -t ai-job-search-agent .
docker run --rm ai-job-search-agent
```

## Railway

Deployment uses `railway.toml` and the project `Dockerfile`. The default Railway deployment is the scheduled worker, not the local React dashboard.

Set these Railway variables on the worker service:

```text
DATABASE_URL=${{Postgres.DATABASE_URL}}
SETTINGS_ENCRYPTION_KEY=<generated-value>
JOB_SEARCH_TIME_ZONE=Europe/London
JOB_SEARCH_RUN_AT=10:00
JOB_SEARCH_POSTCODE=MK4 4QG
JOB_SEARCH_RADIUS_MILES=50
JOB_SEARCH_POSTED_WITHIN_DAYS=7
JOB_SEARCH_MIN_PERMANENT_SALARY_GBP=75000
JOB_SEARCH_MIN_CONTRACT_DAY_RATE_GBP=400
JOB_SEARCH_MIN_CONTRACT_MONTHS=6
JOB_SEARCH_EXCLUDED_KEYWORDS=graduate,junior,java only,onsite 5 days,5 days onsite,sc clearance
JOB_SEARCH_DESIRED_DESIGNATION=Senior Software Engineer,Lead Developer,Principal Engineer
JOB_SEARCH_SKILLS=c#,asp.net core,react,typescript,aws,docker
REED_API_KEY=<redacted>
SLACK_WEBHOOK_URL=<redacted>
GMAIL_CREDENTIALS_JSON=<redacted>
GMAIL_USER_EMAIL=you@your-domain.com
GMAIL_SEARCH_QUERY=label:job-alerts is:unread
JOB_AGENT_HTTP_HOST=localhost
JOB_AGENT_API_KEY=<set when exposing the HTTP API beyond localhost>
```

The HTTP settings API and React dashboard are local developer tooling unless you add separate Railway web services for `AiJobSearchAgent.McpServer` and the frontend.

See `docs/railway-deployment.md`.
