# AI Job Search Agent

Automated job search agent for senior UK software roles matched against Ekanatha Reddy Urivakili's CV profile.

## What is built

- `.NET 10` worker with daily scheduler (Europe/London timezone)
- Reed API job source adapter (live)
- Gmail job alert source adapter — reads job alert emails via Google service account credentials (live)
- Source policy guard — per-source enable/disable and rate limiting
- Deterministic filter engine — salary, day rate, location, title, recency
- CV keyword scorer — core skills, domain, leadership signals
- Markdown report generator — writes daily file to `/reports`
- Slack reporter — posts strong matches (score ≥ 85) via incoming webhook
- Dual-mode MCP server — STDIO for AI clients, HTTP for the React dashboard
- React/Vite dashboard — filter, sort, paginate, and open job adverts
- Settings panel — configure Slack webhook and Gmail credentials from the UI, saved to `.env`
- PostgreSQL schema, Docker Compose, Railway deployment

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
        ConfigApi["Config API\nGET/POST /api/config"]
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
        IndeedAdapter["IndeedAlertJobSourceAdapter\n(Indeed alert emails via Gmail)"]
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
    HttpApi --> McpService
    StdioMcp --> McpService
    McpService --> Orchestrator
    Scheduler --> Orchestrator
    ConfigApi --> CredProvider

    Orchestrator --> PolicyGuard
    Orchestrator --> ReedAdapter
    Orchestrator --> GmailAdapter
    Orchestrator --> IndeedAdapter
    Orchestrator --> FilterEngine
    FilterEngine --> Scorer
    Scorer --> Dedup
    Dedup --> MdReporter
    Dedup --> SlackReporter

    GmailAdapter --> Gmail[("Gmail API")]
    IndeedAdapter --> Gmail
    ReedAdapter --> ReedAPI[("Reed API")]
    SlackReporter --> Slack[("Slack Webhook")]
    MdReporter --> Reports[("reports/")]
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

        DevUI -- "GET /api/jobs/search" --> DevMcp
        DevUI -- "GET/POST /api/config" --> DevMcp
        DevMcp --> LocalPg
        DevWorker --> LocalPg
        DevMcp --> DotEnv
        DevWorker --> DotEnv
    end

    subgraph Railway["Railway Production"]
        RailwayMcp["McpServer --http\n(PORT env var)"]
        RailwayWorker["Worker --schedule"]
        RailwayPg["Managed PostgreSQL"]
        RailwaySecrets["Railway Env Vars\n(secrets)"]

        RailwayMcp --> RailwayPg
        RailwayWorker --> RailwayPg
        RailwayMcp --> RailwaySecrets
        RailwayWorker --> RailwaySecrets
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
    Employment -- Permanent --> Salary{"Salary >= minimum?"}
    Employment -- Contract --> Rate{"Day rate >= minimum?"}
    Salary -- No --> RejectSalary["Reject: below salary floor"]
    Rate -- No --> RejectRate["Reject: below rate floor"]
    Rate -- Yes --> Duration{"Duration >= min months?"}
    Duration -- No --> RejectDuration["Reject: short contract"]
    Salary -- Yes --> Score["Score against CV keywords\n(core skills, domain, leadership)"]
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
    participant Orchestrator
    participant Reed as Reed API
    participant Gmail as Gmail API
    participant Scorer as Filter + Scorer

    Client->>Server: Search (HTTP GET /api/jobs/search or MCP tool)
    Server->>Orchestrator: RunAsync(criteria, profile)

    Orchestrator->>Reed: FetchAsync
    Reed-->>Orchestrator: JobPostings[]

    Orchestrator->>Gmail: FetchAsync (service account credentials)
    Gmail-->>Orchestrator: JobPostings[] (parsed from alert emails)

    Orchestrator->>Scorer: Filter + score all jobs
    Scorer-->>Orchestrator: JobMatches[]

    Orchestrator->>Orchestrator: Deduplicate by source|sourceJobId
    Orchestrator-->>Server: SearchRunResult
    Server-->>Client: Matches (JSON)
```

## Search Criteria

| Setting | Default | Env var |
|---|---|---|
| Postcode | `MK4 4QG` | `JOB_SEARCH_POSTCODE` |
| Radius | 50 miles | `JOB_SEARCH_RADIUS_MILES` |
| Posted within | 7 days | `JOB_SEARCH_POSTED_WITHIN_DAYS` |
| Min permanent salary | £75,000/yr | `JOB_SEARCH_MIN_PERMANENT_SALARY_GBP` |
| Min contract day rate | £400/day | `JOB_SEARCH_MIN_CONTRACT_DAY_RATE_GBP` |
| Min contract duration | 6 months | `JOB_SEARCH_MIN_CONTRACT_MONTHS` |
| Run time | 10:00 Europe/London | `JOB_SEARCH_RUN_AT` |
| Indeed alert query | `from:jobalerts-noreply@indeed.com is:unread` | `INDEED_GMAIL_SEARCH_QUERY` |

Target titles: Senior Software Engineer, Senior Fullstack Engineer, Senior Software Developer, Lead Developer, Lead Software Engineer, Principal Engineer, Principal Developer.

## Configuration

Copy `.env.example` to `.env` and fill in the secrets. The HTTP server reads this file at startup via `CredentialProvider`. All values can also be set as real environment variables (those take priority over the file).

### Slack Webhook URL

A Slack incoming webhook URL. In your Slack workspace: **Settings → Integrations → Incoming Webhooks → Add New Webhook**.

```
SLACK_WEBHOOK_URL=https://hooks.slack.com/services/T00000000/B00000000/XXXXXXXXXXXXXXXXXXXXXXXX
```

The worker posts a message per run when any job scores ≥ 85. If the variable is absent or blank, Slack reporting is silently skipped.

### Gmail Credentials JSON

A Google **service account** credentials JSON file, pasted inline as a single value. The service account needs **Gmail API** access (`gmail.readonly` scope) granted to it.

1. Go to Google Cloud Console → **IAM & Admin → Service Accounts → Create Service Account**.
2. Enable the **Gmail API** on the project.
3. Under the service account, create a **JSON key** and download it.
4. Grant the service account access to your Gmail (domain-wide delegation for Workspace, or direct sharing for personal accounts).

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
```

If the variable is absent or blank, the Gmail source adapter returns an empty result with a warning — the rest of the run continues normally.

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

### Indeed Job Alert Emails

Indeed job alerts are ingested from your Gmail inbox using the same service account as the Gmail adapter. Set up a saved search on Indeed and enable email job alerts for your account.

Set `INDEED_GMAIL_SEARCH_QUERY` to target Indeed alert emails specifically:

```
INDEED_GMAIL_SEARCH_QUERY=from:jobalerts-noreply@indeed.com is:unread
```

The adapter extracts the stable Indeed job key (`jk` parameter) from each alert link and maps it to a canonical `https://uk.indeed.com/viewjob?jk=...` URL. Salary, location, employment type, and description are parsed from the email HTML. If `GMAIL_CREDENTIALS_JSON` is configured, Indeed is automatically enabled.

### Settings Panel

The React dashboard includes a **Settings** screen (top-right button) where you can enter and save these three values without touching the `.env` file manually. Changes are written to the `.env` file in the server's working directory via `POST /api/config`.

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

Open the Vite URL (default `http://localhost:5173`). Use the Settings button to configure Slack and Gmail credentials.

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

## Railway

Deployment uses `railway.toml` and the project `Dockerfile`. Set `REED_API_KEY`, `SLACK_WEBHOOK_URL`, `GMAIL_CREDENTIALS_JSON`, `GMAIL_SEARCH_QUERY`, and `DATABASE_URL` as Railway environment variables.

See `docs/railway-deployment.md`.
