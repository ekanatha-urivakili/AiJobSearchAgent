# AI Job Search Agent

Automated job search agent for senior UK software roles matched against Ekanatha Reddy Urivakili's CV profile.

## Current Status

- .NET worker app
- source policy guard
- sample Reed and JobServe source adapters
- Indeed disabled by default until approved access is confirmed
- deterministic filtering
- CV keyword scoring
- Markdown report generation
- dependency-free console test runner
- Docker and Docker Compose support
- PostgreSQL schema initialization
- Railway deployment configuration
- architecture document with HLD, LLD, sequence diagrams, flow charts, ADRs, QA, and DevOps plan

## Search Criteria

- Location: MK4 4QG within 50 miles
- Posted date: last 7 days
- Titles: Senior Software Engineer, Senior Fullstack Engineer, Lead Developer, Senior Software Developer
- Employment: permanent or contract
- Work modes: remote, hybrid, office
- Permanent salary: minimum GBP 75,000 per year
- Contract rate: minimum GBP 400 per day
- Contract duration: minimum 6 months

## Run Locally

```bash
dotnet run --project src/AiJobSearchAgent.Worker/AiJobSearchAgent.Worker.csproj
```

## Run Continuously

```bash
dotnet run --project src/AiJobSearchAgent.Worker/AiJobSearchAgent.Worker.csproj -- --schedule
```

Scheduler mode waits until 10:00 Europe/London, runs the agent, then repeats daily.

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

See `/Users/ekanathareddyurivakili/Documents/AiJobSearchAgent/docs/railway-deployment.md`.

## Web UI

The React/Vite UI lives in `/Users/ekanathareddyurivakili/Documents/AiJobSearchAgent/frontend`.

Run it locally:

```bash
cd frontend
npm install
npm run dev
```

Open the local Vite URL and click `Open job advert` on any result to go to the original job website in a new tab.
The UI expects each result's `url` to be the exact job-description URL from the crawler, not a search page or source homepage.

## Architecture

See `/Users/ekanathareddyurivakili/Documents/GitHub/AiJobSearchAgent/docs/job-search-agent-architecture.md`.

MCP job-site integration plan: `/Users/ekanathareddyurivakili/Documents/GitHub/AiJobSearchAgent/docs/mcp-job-sites-integration-plan.md`.
