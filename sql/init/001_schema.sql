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

CREATE TABLE IF NOT EXISTS source_policies (
    source_name TEXT PRIMARY KEY,
    fetch_mode TEXT NOT NULL,
    enabled BOOLEAN NOT NULL,
    minimum_delay_seconds INTEGER NOT NULL,
    last_reviewed_on DATE NOT NULL
);

INSERT INTO source_policies (source_name, fetch_mode, enabled, minimum_delay_seconds, last_reviewed_on)
VALUES
    ('Reed', 'ApprovedApi', TRUE, 3, DATE '2026-06-03'),
    ('Gmail Alerts', 'AlertInbox', TRUE, 0, DATE '2026-06-03'),
    ('Indeed UK', 'AlertInbox', FALSE, 10, DATE '2026-06-03')
ON CONFLICT (source_name) DO NOTHING;
