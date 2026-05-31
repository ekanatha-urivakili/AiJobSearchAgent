# AI Job Search Agent

Automated job search agent for senior UK software roles matched against Ekanatha Reddy Urivakili's CV profile.

The project is designed to run every morning at 10:00 UK time, collect jobs posted in the last 7 days, filter them against hard constraints, score them against the CV, and produce a daily Markdown report.

## Current Status

MVP implementation:

- .NET worker app
- source policy guard
- sample Reed and JobServe source adapters
- Indeed disabled by default until approved access is confirmed
- deterministic filtering
- CV keyword scoring
- Markdown report generation
- dependency-free console test runner
- architecture document with HLD, LLD, sequence diagrams, flow charts, ADRs, QA, and DevOps plan

## Search Criteria

- Location: MK4 4QG within 50 miles
- Posted date: last 7 days
- Titles:
  - Senior Software Engineer
  - Senior Fullstack Engineer
  - Lead Developer
  - Senior Software Developer
- Employment: permanent or contract
- Work modes: remote, hybrid, office
- Permanent salary: minimum GBP 75,000 per year
- Contract rate: minimum GBP 400 per day
- Contract duration: minimum 6 months

## Compliance Position

This project uses a compliance-first adapter model.

- Prefer official APIs, approved feeds, saved searches, or job alert email ingestion.
- Use public page crawling only where the source terms and `robots.txt` permit it.
- Do not bypass CAPTCHA, login walls, anti-bot controls, paywalls, or disallowed paths.
- Indeed is disabled by policy in the MVP until approved access is available.
- Reed `/api/` paths must not be called by the crawler.

## Run Locally

```bash
dotnet run --project src/AiJobSearchAgent.Worker/AiJobSearchAgent.Worker.csproj
```

The worker writes a report under the build output `reports` directory and prints the path.

## Run Continuously

```bash
dotnet run --project src/AiJobSearchAgent.Worker/AiJobSearchAgent.Worker.csproj -- --schedule
```

Scheduler mode waits until 10:00 Europe/London, runs the agent, then repeats daily.

The included GitHub Actions workflow runs at `09:00 UTC`, which aligns with `10:00 Europe/London` during British Summer Time. For year-round exact UK local time, prefer the worker `--schedule` mode in a long-running container.

## Run Tests

```bash
dotnet run --project tests/AiJobSearchAgent.Tests/AiJobSearchAgent.Tests.csproj
```

## Build

```bash
dotnet build AiJobSearchAgent.slnx
```

## Docker

```bash
docker build -t ai-job-search-agent .
docker run --rm ai-job-search-agent
```

## Docker Compose With PostgreSQL

Start PostgreSQL:

```bash
docker compose up -d postgres
```

Run PostgreSQL and the worker container:

```bash
docker compose --profile worker up --build
```

The local PostgreSQL container initializes the schema from `sql/init/001_schema.sql`.

## Railway

Railway deployment uses `railway.toml` and the project `Dockerfile`.

See `/Users/ekanathareddyurivakili/Documents/GitHub/AiJobSearchAgent/docs/railway-deployment.md`.

## Architecture

See `/Users/ekanathareddyurivakili/Documents/GitHub/AiJobSearchAgent/docs/job-search-agent-architecture.md`.

## Roadmap

1. Replace sample adapters with approved source adapters.
2. Add alert email inbox ingestion.
3. Persist runs and jobs in PostgreSQL.
4. Add LLM-backed scoring behind a structured scorer interface.
5. Send daily report by email or Teams.
6. Deploy as a scheduled container.
