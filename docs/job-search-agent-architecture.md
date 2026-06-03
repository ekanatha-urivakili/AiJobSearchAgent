# AI Job Search Agent Architecture

## Current Repository Reality

This repository contains a full .NET and React implementation of an automated job search agent:

- `src/AiJobSearchAgent.Core`: Domain models, filtering, and scoring logic.
- `src/AiJobSearchAgent.McpServer`: Dual-mode server (MCP STDIO for AI clients, HTTP for the Web UI), settings API, CV upload API, and live source integrations.
- `src/AiJobSearchAgent.Worker`: Daily scheduler and reporter.
- `frontend/`: React/Vite/TypeScript dashboard.
- `tests/AiJobSearchAgent.Tests`: Regression tests for filtering and scoring.
- `docker-compose.yml` & `Dockerfile`: Containerization.
- `railway.toml`: Deployment configuration.
- `sql/init/001_schema.sql`: PostgreSQL schema.
- `CVs/`: Local CV upload storage for `.pdf`, `.docx`, and `.md` files.

## High-Level Design

```mermaid
flowchart TD
    subgraph Clients["Clients"]
        WebUI["Web UI\n(React/Vite)"]
        McpClient["MCP Client\n(Claude Desktop, etc)"]
    end

    subgraph Server["AiJobSearchAgent.McpServer"]
        HttpApi["HTTP API\n(--http)"]
        StdioMcp["STDIO MCP Server\n(default)"]
        McpService["Job Search Service"]
        ConfigApi["Settings API\nGET/POST /api/config"]
        CvApi["CV API\nGET /api/cvs\nPOST /api/cvs/upload"]
        SettingsRepo["SettingsRepository\napp_settings + AES-GCM"]
    end

    subgraph Orchestration["Core Engine"]
        Worker["AiJobSearchAgent.Worker\n(Daily Scheduler)"]
        Orchestrator["Job Search Orchestrator"]
        Filter["Deterministic Filter"]
        Scorer["CV Match Scorer"]
    end

    subgraph Sources["Job Sources"]
        ReedApi["Reed API Adapter"]
        Gmail["Gmail Alert Adapter"]
        Indeed["Indeed Alert Adapter\n(via Gmail alerts)"]
    end

    WebUI --> HttpApi
    WebUI --> ConfigApi
    WebUI --> CvApi
    McpClient --> StdioMcp
    HttpApi --> McpService
    StdioMcp --> McpService
    ConfigApi --> SettingsRepo
    CvApi --> CvFolder[("CVs/")]
    McpService --> Orchestrator
    Worker --> Orchestrator

    Orchestrator --> ReedApi
    Orchestrator --> Gmail
    Orchestrator --> Indeed
    Orchestrator --> Filter
    Orchestrator --> Scorer

    SettingsRepo --> PostgreSQL[("PostgreSQL")]
    Orchestrator --> Reports["Markdown Reports"]
```

## Deployment View

```mermaid
flowchart TB
    subgraph Local["Local Development"]
        DevMcp["dotnet run McpServer"]
        DevWorker["dotnet run Worker"]
        DevUI["npm run dev"]
        LocalPg["Docker PostgreSQL"]
        LocalCvs["CVs/"]
        DevMcp --> LocalPg
        DevWorker --> LocalPg
        DevMcp --> LocalCvs
        DevUI -- fetch --> DevMcp
    end

    subgraph Railway["Railway Production"]
        RailwayWorker["Worker Service\n(Dockerfile + --schedule)"]
        RailwayPg["Managed PostgreSQL"]
        RailwayVars["Railway Env Vars"]
        RailwayWorker --> RailwayPg
        RailwayWorker --> RailwayVars
    end

    subgraph OptionalWeb["Optional Web Deployment"]
        RailwayMcp["McpServer Service\n(HTTP Mode)"]
        Frontend["Frontend Service"]
        Frontend --> RailwayMcp
        RailwayMcp --> RailwayPg
    end
```

## Logical Low-Level Design

```mermaid
flowchart TD
    App["Entrypoint"] --> Config["Configuration\n(Env Vars)"]
    App --> Settings["Settings API\n(PostgreSQL app_settings)"]
    App --> CvUpload["CV Upload API\n(CVs folder)"]
    Config --> Criteria["Search Criteria"]
    Settings --> Criteria
    App --> Orchestrator["Orchestrator"]
    Orchestrator --> Policy["Source Policy Guard"]
    Orchestrator --> Adapters["Source Adapters"]
    Adapters --> RawJobs["Raw Job Postings"]
    RawJobs --> Filter["Job Filter Engine"]
    Filter --> Scorer["CV Match Scorer"]
    Scorer --> Match["Job Match"]
    Match --> Dedupe["Deduplicator\n(Source|SourceJobId)"]
    Dedupe --> Report["Report Generator"]
```

### Core Components

| Component | Responsibility | Status |
|---|---|---|
| Core Engine | Filtering, scoring, and orchestration. | Production-ready |
| MCP Server | Provides tools/resources to AI clients via STDIO. | Live |
| HTTP API | Provides data to the Web UI. | Live (in McpServer) |
| Settings API | Saves configurable settings, encrypting secrets in PostgreSQL. | Live |
| CV Upload API | Lists and uploads `.pdf`, `.docx`, and `.md` CV files into `CVs/`. | Live |
| Worker | Daily scheduled runs and local reports. | Live |
| Reed Adapter | Fetches and maps jobs from Reed API. | Live |
| Gmail Adapter | Reads Gmail job alerts using configured service account JSON. | Live |
| Indeed Adapter | Parses Indeed alert emails from Gmail. | Live |
| Deduplicator | Ensures unique results by source job ID. | Live |

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

    app_settings {
        text key PK
        text value
        boolean is_secret
        timestamptz updated_at
    }
```

## Run Sequence (HTTP/MCP)

```mermaid
sequenceDiagram
    autonumber
    participant Client as Web UI / MCP Client
    participant Server as McpServer
    participant Settings as SettingsRepository
    participant Orchestrator
    participant Reed as Reed API
    participant Gmail as Gmail API
    participant Scorer as Filter/Scorer

    Client->>Server: Request Search (HTTP or MCP Tool)
    Server->>Settings: Load saved settings (HTTP startup/config)
    Server->>Orchestrator: RunAsync(Criteria)
    Orchestrator->>Reed: FetchAsync
    Reed-->>Orchestrator: JobPostings
    Orchestrator->>Gmail: FetchAsync (Gmail + Indeed alerts)
    Gmail-->>Orchestrator: JobPostings
    Orchestrator->>Scorer: Evaluate & Score
    Scorer-->>Orchestrator: JobMatches
    Orchestrator->>Orchestrator: Deduplicate (Source|ID)
    Orchestrator-->>Server: SearchRunResult
    Server-->>Client: Matches & Report Link
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

## Architecture Documentation

See `docs/job-search-agent-architecture.md` (this file).

MCP job-site integration plan: `docs/mcp-job-sites-integration-plan.md`.
