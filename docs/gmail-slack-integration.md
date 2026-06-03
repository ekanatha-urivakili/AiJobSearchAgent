# Gmail & Slack Integration — Architecture Design

**Status:** Draft for review  
**Date:** 2026-06-02  
**Author:** Principal Engineering Review

---

## 1. Architectural Opinion on the Submitted Plan

The submitted plan is directionally correct. The `IJobSourceAdapter` reuse for Gmail and the `IJobReporter` abstraction are both sound. Before building, four structural decisions need to be made explicit:

### 1.1 Decisions to Lock In Before Coding

| # | Decision | Recommendation | Risk if Deferred |
|---|----------|----------------|-----------------|
| D-1 | Gmail auth strategy | Offline OAuth with stored refresh token (file in dev, base64 env var in CI/Railway). **Not** Service Account — Gmail API only supports SA for Google Workspace domains, not personal Gmail. | Wrong auth strategy means complete rewrite of credential handling |
| D-2 | Reporter invocation point | Reporters should be called from the **Worker** (`Program.cs`), not injected into `JobSearchOrchestrator`. Orchestrator's responsibility stops at `SearchRunResult`. | Violates single responsibility; Orchestrator becomes aware of I/O concerns |
| D-3 | Per-provider email parser | Implement a `Dictionary<string, IEmailBodyParser>` keyed on sender domain. One parser per provider. **Not** a single monolithic parser. | Adding a second provider (Indeed, LinkedIn) requires touching existing parsing code |
| D-4 | Cross-source deduplication | Gmail-parsed jobs need a deterministic `SourceJobId` derived from the job URL (e.g. `SHA256(url)[0..8]`). Otherwise the existing `Deduplicate()` in `JobSearchOrchestrator` won't catch overlap with API-sourced jobs from the same provider. | Duplicate jobs appear in output; user gets confused |

### 1.2 What the Plan Gets Right

- `IJobReporter` is the correct abstraction. Multiple reporters (Markdown, Slack) run sequentially after the search.
- `FetchMode.AlertInbox` is already present in `Domain.cs` and `Defaults.cs` — the policy guard is already wired for this flow.
- Slack via incoming webhooks (not the Bot API) is right for a worker process. No OAuth, just a POST.
- Deterministic regex/HTML parsing is the right call over LLM-based extraction — faster, free, and the email structure from providers is stable enough.

### 1.3 What Needs Strengthening

- The plan lacks a **token storage strategy** for Gmail OAuth. This is the most operationally complex part.
- The plan lacks **error isolation**: a Slack webhook failure or Gmail auth failure must not fail the entire search run.
- The plan merges all three classes (`IJobReporter`, `MarkdownFileJobReporter`, `SlackJobReporter`) into a single `Reporting.cs`. This works but should be noted as a deliberate choice given the project's flat-file structure.

---

## 2. High-Level Design (HLD)

### 2.1 System Context

```mermaid
C4Context
    title System Context — AiJobSearchAgent with Gmail & Slack

    Person(user, "Job Seeker", "Ekanatha Reddy Urivakili")

    System(agent, "AiJobSearchAgent", "Searches, scores, and reports job matches")

    System_Ext(gmail, "Gmail", "Receives job alert emails from Reed, JobServe, Indeed, LinkedIn")
    System_Ext(reed_api, "Reed API", "Approved job search API")
    System_Ext(slack, "Slack", "Team/personal workspace for notifications")
    System_Ext(google_oauth, "Google OAuth 2.0", "Issues access tokens for Gmail API")

    Rel(user, agent, "Triggers daily via GitHub Actions / schedule flag")
    Rel(agent, gmail, "Reads unread job alert emails", "Gmail API v1 (HTTPS)")
    Rel(agent, reed_api, "Searches for jobs matching criteria", "HTTPS REST")
    Rel(agent, slack, "Posts high-score matches", "Incoming Webhook (HTTPS POST)")
    Rel(agent, google_oauth, "Exchanges refresh token for access token", "HTTPS")
    Rel(gmail, user, "Email alerts from job boards", "SMTP")
```

### 2.2 Component Architecture

```mermaid
graph TB
    subgraph Worker["AiJobSearchAgent.Worker"]
        P["Program.cs\n(Composition Root)"]
    end

    subgraph Core["AiJobSearchAgent.Core"]
        subgraph Sources
            ISA["IJobSourceAdapter"]
            GJSA["GmailAlertJobSourceAdapter"]
            SJSA["SampleJobSourceAdapter"]
        end

        subgraph Orchestration
            JO["JobSearchOrchestrator"]
            SPG["SourcePolicyGuard"]
            JFE["JobFilterEngine"]
            CMS["CvMatchScorer"]
        end

        subgraph Reporting
            IJR["IJobReporter"]
            MFR["MarkdownFileJobReporter\n(wraps existing MarkdownReportGenerator)"]
            SJR["SlackJobReporter"]
        end

        subgraph Gmail
            GCA["GoogleCredentialAuthenticator"]
            PRP["ProviderParserRegistry"]
            REP["ReedEmailParser"]
            JSP["JobServeEmailParser"]
        end
    end

    P --> GJSA
    P --> SJSA
    P --> JO
    P --> MFR
    P --> SJR

    GJSA --> GCA
    GJSA --> PRP
    PRP --> REP
    PRP --> JSP

    JO --> ISA
    JO --> SPG
    JO --> JFE
    JO --> CMS

    GJSA -.->|implements| ISA
    SJSA -.->|implements| ISA
    MFR -.->|implements| IJR
    SJR -.->|implements| IJR
```

### 2.3 End-to-End Data Flow

```mermaid
sequenceDiagram
    actor GH as GitHub Actions
    participant W as Worker (Program.cs)
    participant JO as JobSearchOrchestrator
    participant GJSA as GmailAlertJobSourceAdapter
    participant GAuth as GoogleCredentialAuthenticator
    participant GmailAPI as Gmail API
    participant ReedAPI as Reed API
    participant Scorer as CvMatchScorer
    participant MFR as MarkdownFileJobReporter
    participant SJR as SlackJobReporter
    participant Slack as Slack Webhook

    GH->>W: Trigger (cron or --schedule flag)
    W->>JO: RunAsync(criteria, profile)

    loop For each enabled source
        JO->>GJSA: FetchAsync(criteria, ct)
        GJSA->>GAuth: GetAccessTokenAsync()
        GAuth-->>GJSA: access_token (from refresh token)
        GJSA->>GmailAPI: users.messages.list (label:job-alerts is:unread)
        GmailAPI-->>GJSA: [messageId, ...]
        loop For each message
            GJSA->>GmailAPI: users.messages.get(messageId)
            GmailAPI-->>GJSA: raw email (base64)
            GJSA->>GJSA: ParseEmailToJobPostings()
        end
        GJSA->>GmailAPI: users.messages.batchModify (mark as read)
        GJSA-->>JO: SourceFetchResult([JobPosting, ...])
    end

    JO->>JO: Deduplicate(allJobs)
    JO->>Scorer: Score(job, profile) for each candidate
    JO-->>W: SearchRunResult

    W->>MFR: ReportAsync(result, criteria)
    MFR->>MFR: Generate Markdown
    MFR-->>W: File written to /reports/

    W->>SJR: ReportAsync(result, criteria)
    SJR->>SJR: Format Block Kit payload (score >= 70 only)
    SJR->>Slack: POST /services/xxx/yyy/zzz
    Slack-->>SJR: 200 OK / error
    SJR-->>W: (log warning on failure, do not throw)

    W-->>GH: Exit 0
```

---

## 3. Low-Level Design (LLD)

### 3.1 Interface & Contract Definitions

#### 3.1.1 `IJobReporter`

```csharp
// src/AiJobSearchAgent.Core/Reporting.cs

public interface IJobReporter
{
    // Must not throw. Log and return on failure.
    Task ReportAsync(
        SearchRunResult result,
        JobSearchCriteria criteria,
        CancellationToken cancellationToken);
}
```

**Design note:** Reporters are fire-and-forget from the Worker's perspective. A Slack POST failure must not abort the run or surface as an exception to the caller. Each implementation logs its own errors.

#### 3.1.2 `IEmailBodyParser`

```csharp
// src/AiJobSearchAgent.Core/Gmail.cs (new file)

public interface IEmailBodyParser
{
    // The sender domain this parser handles, e.g. "reed.co.uk"
    string SenderDomain { get; }

    IReadOnlyCollection<JobPosting> Parse(string htmlBody, DateOnly today);
}
```

#### 3.1.3 `IGoogleCredentialProvider`

```csharp
// src/AiJobSearchAgent.Core/Gmail.cs

public interface IGoogleCredentialProvider
{
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken);
}
```

---

### 3.2 Class Design

#### 3.2.1 Reporting Layer

```mermaid
classDiagram
    class IJobReporter {
        <<interface>>
        +ReportAsync(result, criteria, ct) Task
    }

    class MarkdownFileJobReporter {
        -MarkdownReportGenerator generator
        -string outputDirectory
        +ReportAsync(result, criteria, ct) Task
    }

    class SlackJobReporter {
        -HttpClient http
        -string webhookUrl
        -int minimumScore
        +ReportAsync(result, criteria, ct) Task
        -FormatPayload(matches, criteria) string
        -FormatMatch(match) object
    }

    IJobReporter <|.. MarkdownFileJobReporter
    IJobReporter <|.. SlackJobReporter
    MarkdownFileJobReporter --> MarkdownReportGenerator
```

#### 3.2.2 Gmail Source Layer

```mermaid
classDiagram
    class IJobSourceAdapter {
        <<interface>>
        +SourceName string
        +FetchAsync(criteria, ct) Task~SourceFetchResult~
    }

    class GmailAlertJobSourceAdapter {
        -IGoogleCredentialProvider credentialProvider
        -ProviderParserRegistry parserRegistry
        -string gmailQuery
        +SourceName string
        +FetchAsync(criteria, ct) Task~SourceFetchResult~
        -FetchMessageIdsAsync(token, ct) Task~List~string~~
        -FetchAndParseMessageAsync(token, msgId, ct) Task~List~JobPosting~~
        -ExtractSenderDomain(message) string
        -DeriveSourceJobId(url) string
    }

    class ProviderParserRegistry {
        -Dictionary~string, IEmailBodyParser~ parsers
        +Register(parser) void
        +TryGet(senderDomain, out parser) bool
    }

    class IEmailBodyParser {
        <<interface>>
        +SenderDomain string
        +Parse(htmlBody, today) IReadOnlyCollection~JobPosting~
    }

    class ReedEmailParser {
        +SenderDomain string = "reed.co.uk"
        +Parse(htmlBody, today) IReadOnlyCollection~JobPosting~
        -ExtractJobBlocks(doc) IEnumerable~HtmlNode~
        -ParseJobBlock(node, today) JobPosting?
    }

    class JobServeEmailParser {
        +SenderDomain string = "jobserve.com"
        +Parse(htmlBody, today) IReadOnlyCollection~JobPosting~
    }

    class GoogleCredentialProvider {
        -string refreshToken
        -string clientId
        -string clientSecret
        +GetAccessTokenAsync(ct) Task~string~
    }

    class IGoogleCredentialProvider {
        <<interface>>
        +GetAccessTokenAsync(ct) Task~string~
    }

    IJobSourceAdapter <|.. GmailAlertJobSourceAdapter
    GmailAlertJobSourceAdapter --> ProviderParserRegistry
    GmailAlertJobSourceAdapter --> IGoogleCredentialProvider
    ProviderParserRegistry --> IEmailBodyParser
    IEmailBodyParser <|.. ReedEmailParser
    IEmailBodyParser <|.. JobServeEmailParser
    IGoogleCredentialProvider <|.. GoogleCredentialProvider
```

---

### 3.3 Gmail Authentication Flow

The worker runs unattended (GitHub Actions / Railway). OAuth with a **stored refresh token** is the only viable approach for a personal Gmail account.

```mermaid
sequenceDiagram
    participant Dev as Developer (one-time setup)
    participant Browser as Browser
    participant GConsole as Google Cloud Console
    participant OAuthHelper as OAuthSetupHelper (CLI tool or script)
    participant GAuth as Google OAuth 2.0
    participant EnvStore as .env / Railway Secret

    Dev->>GConsole: Create OAuth 2.0 Client ID (Desktop type)
    GConsole-->>Dev: client_id, client_secret

    Dev->>OAuthHelper: Run setup helper with client_id + client_secret
    OAuthHelper->>GAuth: Authorization URL (scope: gmail.readonly, gmail.labels)
    GAuth-->>Browser: Redirect to consent screen
    Browser->>Dev: User grants consent
    GAuth-->>OAuthHelper: authorization_code
    OAuthHelper->>GAuth: Exchange code for tokens
    GAuth-->>OAuthHelper: access_token + refresh_token

    OAuthHelper-->>Dev: Print refresh_token
    Dev->>EnvStore: Store GMAIL_REFRESH_TOKEN, GMAIL_CLIENT_ID, GMAIL_CLIENT_SECRET

    Note over Dev,EnvStore: One-time setup complete. Worker uses refresh token at runtime.
```

**Runtime token refresh (per-run):**

```mermaid
sequenceDiagram
    participant GJSA as GmailAlertJobSourceAdapter
    participant GCP as GoogleCredentialProvider
    participant GAuth as Google OAuth Token Endpoint

    GJSA->>GCP: GetAccessTokenAsync()
    GCP->>GAuth: POST /token (grant_type=refresh_token, refresh_token, client_id, client_secret)
    GAuth-->>GCP: { access_token, expires_in }
    GCP-->>GJSA: access_token
```

---

### 3.4 Email Parsing — Reed Alert Format

Reed job alert emails follow a predictable structure. The parser targets the stable HTML block pattern.

```mermaid
flowchart TD
    A[Raw email body - base64] --> B[Decode UTF-8]
    B --> C{Content-Type?}
    C -->|text/html| D[Load into HtmlAgilityPack]
    C -->|multipart| E[Extract text/html part]
    E --> D
    D --> F[Select all job card divs\nXPath: //div\[contains @class 'job'\]]
    F --> G{Any nodes found?}
    G -->|No| H[Return empty + warning]
    G -->|Yes| I[For each node]
    I --> J[Extract: title, company, location, salary, url]
    J --> K{URL present?}
    K -->|No| L[Skip node]
    K -->|Yes| M[DeriveSourceJobId = SHA256 of URL first 8 chars]
    M --> N[Construct JobPosting\nSource=Reed, FetchMode=AlertInbox]
    N --> O[Add to result list]
    O --> I
```

**`DeriveSourceJobId` — cross-source deduplication:**

```csharp
private static string DeriveSourceJobId(Uri url)
{
    var bytes = System.Security.Cryptography.SHA256.HashData(
        System.Text.Encoding.UTF8.GetBytes(url.AbsoluteUri));
    return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
}
```

This ensures if the Reed API and the Reed email alert return the same job URL, `JobSearchOrchestrator.Deduplicate()` will collapse them — because the `SourceJobId` will match (both keyed as `"reed|{same-hash}"`).

---

### 3.5 Slack Reporter — Block Kit Payload

The reporter only posts jobs with `Score >= 70` (Good Match and above). It groups them into a single message per run.

```mermaid
flowchart TD
    A[ReportAsync called] --> B[Filter: matches where Score >= 70]
    B --> C{Any matches?}
    C -->|No| D[Post summary-only message: '0 matches today']
    C -->|Yes| E[Build Block Kit payload]
    E --> F[Header block: Date + counts]
    F --> G[For each match - max 10]
    G --> H[Section block: title/company/score/link]
    H --> I[Divider block]
    I --> G
    G --> J[POST to SLACK_WEBHOOK_URL]
    J --> K{HTTP 200?}
    K -->|Yes| L[Log success]
    K -->|No| M[Log warning with status code - do NOT throw]
```

**Payload shape:**

```json
{
  "blocks": [
    {
      "type": "header",
      "text": { "type": "plain_text", "text": "Job Matches — 2026-06-02" }
    },
    {
      "type": "section",
      "text": {
        "type": "mrkdwn",
        "text": "*<https://reed.co.uk/...|Senior Software Engineer>* — Example Fintech Ltd\nScore: 87 | Hybrid | Milton Keynes | GBP 80k–90k/yr\n_Tech: c#, asp.net core, react, typescript_"
      }
    },
    { "type": "divider" }
  ]
}
```

---

### 3.6 `Program.cs` — Composition Root Changes

```mermaid
flowchart LR
    A[Program.cs] --> B[Build criteria + profile + policies]
    B --> C[Build GmailAlertJobSourceAdapter]
    C --> D[Build reporters: MarkdownFileJobReporter + SlackJobReporter]
    D --> E[Build JobSearchOrchestrator with all sources]
    E --> F[RunAsync → SearchRunResult]
    F --> G[foreach reporter: await reporter.ReportAsync]
```

The orchestrator constructor does **not** change. Reporters are called sequentially by the Worker after `RunAsync` returns.

---

### 3.7 Configuration Schema

#### New environment variables:

| Variable | Required | Example | Notes |
|----------|----------|---------|-------|
| `GMAIL_CLIENT_ID` | Yes (if Gmail enabled) | `123456.apps.googleusercontent.com` | From Google Cloud Console |
| `GMAIL_CLIENT_SECRET` | Yes (if Gmail enabled) | `GOCSPX-xxxx` | From Google Cloud Console |
| `GMAIL_REFRESH_TOKEN` | Yes (if Gmail enabled) | `1//0g...` | Generated via one-time setup helper |
| `GMAIL_QUERY` | No | `label:job-alerts is:unread` | Default shown; override to restrict scope |
| `SLACK_WEBHOOK_URL` | Yes (if Slack enabled) | `https://hooks.slack.com/services/T.../B.../xxx` | Incoming webhook URL from Slack app |
| `SLACK_MIN_SCORE` | No | `70` | Default 70; only posts matches at or above this score |

#### Updated `.env.example` additions:

```
# Gmail Integration (AlertInbox mode)
# GMAIL_CLIENT_ID=
# GMAIL_CLIENT_SECRET=
# GMAIL_REFRESH_TOKEN=
# GMAIL_QUERY=label:job-alerts is:unread

# Slack Integration
# SLACK_WEBHOOK_URL=
# SLACK_MIN_SCORE=70
```

---

### 3.8 Source Policy Alignment

The existing `Defaults.CreateSourcePolicies()` already marks Reed and JobServe as `FetchMode.AlertInbox`. The Gmail adapter must use the same source names as those policies so `SourcePolicyGuard.CanFetch()` allows them through.

```mermaid
flowchart LR
    A["SourcePolicy('Reed', AlertInbox, Enabled=true)"] --> B{SourcePolicyGuard}
    C["GmailAlertJobSourceAdapter\nSourceName='Reed'"] --> B
    B --> D[CanFetch → Allowed]
```

**Note:** `GmailAlertJobSourceAdapter` is a single adapter that may return jobs from multiple providers (Reed, JobServe) based on which emails are in the inbox. The adapter's `SourceName` property should be set per-job (the `Source` field on `JobPosting`), not globally. Alternatively, register one `GmailAlertJobSourceAdapter` per provider, each with a matching `SourceName`.

**Decision:** Register one adapter per provider. Simpler policy matching, clearer error reporting.

---

## 4. Implementation Phases

### Phase 1 — Reporting Abstraction (no new dependencies)

| Step | File | Change |
|------|------|--------|
| 1.1 | `Core/Reporting.cs` | Add `IJobReporter` interface |
| 1.2 | `Core/Reporting.cs` | Add `MarkdownFileJobReporter` wrapping `MarkdownReportGenerator` |
| 1.3 | `Core/Reporting.cs` | Add `SlackJobReporter` (requires `HttpClient`) |
| 1.4 | `Worker/Program.cs` | Replace direct `MarkdownReportGenerator` call with reporter loop |

**Verify:** Existing output (Markdown file) unchanged. Slack posts if `SLACK_WEBHOOK_URL` is set, silently skips if not.

---

### Phase 2 — Gmail Source Adapter

| Step | File | Change |
|------|------|--------|
| 2.1 | `Core/Core.csproj` | Add `Google.Apis.Gmail.v1`, `Google.Apis.Auth`, `HtmlAgilityPack` |
| 2.2 | `Core/Gmail.cs` | `IEmailBodyParser`, `ProviderParserRegistry`, `IGoogleCredentialProvider`, `GoogleCredentialProvider` |
| 2.3 | `Core/Gmail.cs` | `ReedEmailParser` (first parser) |
| 2.4 | `Core/Gmail.cs` | `GmailAlertJobSourceAdapter` |
| 2.5 | `Core/Defaults.cs` | No change needed — policies already correct |
| 2.6 | `Worker/Program.cs` | Conditionally register `GmailAlertJobSourceAdapter` if Gmail env vars present |

**Verify:** With Gmail env vars set, adapter appears in `SourceResults`. Without them, run proceeds without Gmail (graceful opt-out).

---

### Phase 3 — Tests & Configuration

| Step | File | Change |
|------|------|--------|
| 3.1 | `Tests/SlackJobReporterTests.cs` | Mock `HttpClient`; assert payload shape for score >= 70, empty case, failure isolation |
| 3.2 | `Tests/ReedEmailParserTests.cs` | Parse fixture HTML from `TestData/reed-alert-sample.html` |
| 3.3 | `Tests/GoogleCredentialProviderTests.cs` | Mock HTTP; assert token exchange request shape |
| 3.4 | `.env.example` | Add new variables (documented above) |
| 3.5 | `.github/workflows/daily-job-search.yml` | Add `SLACK_WEBHOOK_URL` and Gmail secrets to env |

---

## 5. Risk & Decisions Log

| # | Risk | Mitigation |
|---|------|-----------|
| R-1 | Reed / JobServe change email HTML structure | Parser is isolated per provider. Only one parser breaks at a time. Add a `Warnings` entry to `SourceFetchResult` when parse yields 0 jobs from a non-empty body. |
| R-2 | Gmail refresh token revoked (user revokes access or token expires after 6 months of inactivity) | `GoogleCredentialProvider` catches auth errors and returns them as a `SourceFetchResult` with an empty job list and a descriptive warning. Does not throw. |
| R-3 | Slack rate limit (max 1 msg/sec per webhook) | Single POST per run. No pagination needed unless > 50 matches, which is unlikely given score threshold. |
| R-4 | Gmail `users.messages.list` returns thousands of emails | Apply `maxResults=50` to the list call. The query `is:unread` and label scoping keeps the list small in practice. Mark as read after processing to prevent re-processing. |
| R-5 | Cross-source duplicate jobs (API + email alert for same job) | `DeriveSourceJobId(url)` produces a stable hash from the canonical URL. The existing `Deduplicate()` in `JobSearchOrchestrator` handles the rest. |
| R-6 | Google OAuth client credentials in CI | Store as GitHub Actions secrets. Never in code or `.env`. Document this in `CONTRIBUTING.md`. |

---

## 6. Out of Scope

- **MCP-based Slack integration** (the Claude MCP server tools): the Worker is a background process; MCP tools require an interactive Claude session. Incoming webhooks are the correct mechanism here.
- **Gmail write operations** (sending replies, creating drafts): read-only scope only.
- **Indeed / LinkedIn email parsers**: architecture supports them (add a new `IEmailBodyParser` implementation), but not in this phase.
- **LLM-based email parsing**: rejected in favour of deterministic regex/HTML parsing for cost, speed, and reliability reasons.
