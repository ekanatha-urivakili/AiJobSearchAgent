import { useEffect, useMemo, useState } from "react";
import { Link, NavLink, Route, Routes, useNavigate, useParams } from "react-router-dom";
import { fetchCvs, fetchJobDetail, fetchMatches, getConfig, saveConfig, uploadCv } from "./api";
import type { CvFile, JobDetail, JobResult, SourceStatus } from "./types";
import type { ReactElement } from "react";

/* ── helpers ─────────────────────────────────────────────────────────────── */

const gbp = new Intl.NumberFormat("en-GB", { style: "currency", currency: "GBP", maximumFractionDigits: 0 });

function formatComp(job: JobResult): string {
  if (job.employmentType === "Contract") {
    const min = job.dayRateMinGbp ? gbp.format(job.dayRateMinGbp) : "TBC";
    const max = job.dayRateMaxGbp ? gbp.format(job.dayRateMaxGbp) : undefined;
    const range = max ? `${min}–${max}/day` : `${min}/day`;
    return job.contractMonths ? `${range} · ${job.contractMonths}mo` : range;
  }
  const min = job.salaryMinGbp ? gbp.format(job.salaryMinGbp) : "TBC";
  const max = job.salaryMaxGbp ? gbp.format(job.salaryMaxGbp) : undefined;
  return max ? `${min}–${max}` : min;
}

function compFloor(job: JobResult): number {
  return job.employmentType === "Contract" ? (job.dayRateMinGbp ?? 0) : (job.salaryMinGbp ?? 0);
}

function fmtDate(v: string): string {
  return new Intl.DateTimeFormat("en-GB", { day: "2-digit", month: "short", year: "numeric" })
    .format(new Date(`${v}T00:00:00Z`));
}

function scoreClass(s: number) { return s >= 85 ? "high" : s >= 70 ? "mid" : ""; }

/* ── context ─────────────────────────────────────────────────────────────── */

type AppState = {
  jobs: JobResult[];
  sourceStatus: SourceStatus[];
  rejectedSummary: Record<string, number>;
  loading: boolean;
  error: string | null;
  reload: () => void;
};

let _appState: AppState | null = null;
const _listeners = new Set<() => void>();

function useAppData(): AppState {
  const [, tick] = useState(0);
  useEffect(() => {
    const cb = () => tick(n => n + 1);
    _listeners.add(cb);
    return () => { _listeners.delete(cb); };
  }, []);
  return _appState!;
}

/* ── data loader (module-level singleton so all routes share one fetch) ───── */

function notifyListeners() { _listeners.forEach(cb => cb()); }

function loadData() {
  _appState = { jobs: [], sourceStatus: [], rejectedSummary: {}, loading: true, error: null, reload: loadData };
  notifyListeners();
  fetchMatches().then(res => {
    if (res.status === "ok") {
      _appState = {
        jobs: res.jobs,
        sourceStatus: res.sourceStatus,
        rejectedSummary: res.rejectedSummary,
        loading: false, error: null, reload: loadData
      };
    } else {
      _appState = { jobs: [], sourceStatus: [], rejectedSummary: {}, loading: false, error: res.message, reload: loadData };
    }
    notifyListeners();
  });
}

loadData();

/* ── TopNav ──────────────────────────────────────────────────────────────── */

function TopNav({ jobCount }: { jobCount: number }): ReactElement {
  return (
    <nav className="topnav">
      <Link to="/" className="nav-brand">
        <span className="nav-brand-icon">⚡</span>
        AI Job Agent
      </Link>
      <ul className="nav-links">
        <li><NavLink to="/"       end className={({ isActive }) => "nav-link" + (isActive ? " active" : "")}>Dashboard</NavLink></li>
        <li><NavLink to="/jobs"       className={({ isActive }) => "nav-link" + (isActive ? " active" : "")}>Browse Jobs</NavLink></li>
        <li><NavLink to="/settings"   className={({ isActive }) => "nav-link" + (isActive ? " active" : "")}>Settings</NavLink></li>
      </ul>
      <div className="nav-end">
        {jobCount > 0 && (
          <span className="nav-badge">
            <span>⚡</span> {jobCount} matches live
          </span>
        )}
      </div>
    </nav>
  );
}

/* ── App (router) ────────────────────────────────────────────────────────── */

export function App(): ReactElement {
  const [, tick] = useState(0);
  useEffect(() => {
    const cb = () => tick(n => n + 1);
    _listeners.add(cb);
    return () => { _listeners.delete(cb); };
  }, []);

  const jobCount = _appState?.jobs.length ?? 0;

  return (
    <div className="app-shell">
      <TopNav jobCount={jobCount} />
      <Routes>
        <Route path="/"             element={<DashboardPage />} />
        <Route path="/jobs"         element={<BrowsePage />} />
        <Route path="/jobs/:source/:sourceJobId" element={<DetailPage />} />
        <Route path="/settings"     element={<SettingsPage />} />
      </Routes>
    </div>
  );
}

/* ═══════════════════════════════════════════════════════════════════════════
   PAGE: Dashboard
   ═══════════════════════════════════════════════════════════════════════════ */

function DashboardPage(): ReactElement {
  const { jobs, sourceStatus, rejectedSummary, loading, error } = useAppData();
  const recommended = jobs.filter(j => j.recommended);
  const avgScore = jobs.length ? Math.round(jobs.reduce((a, j) => a + j.score, 0) / jobs.length) : 0;
  const total = Object.values(rejectedSummary).reduce((a, v) => a + v, 0);

  return (
    <div>
      {/* hero */}
      <div className="hero">
        <div className="hero-eyebrow">Senior UK Software Roles</div>
        <h1>Your AI-Powered Job Intelligence</h1>
        <p className="hero-sub">
          Live-scored job matches from Reed, Indeed, and Gmail alerts — filtered to your CV and salary floor.
        </p>
        {!loading && !error && (
          <div className="hero-stats">
            <div className="stat-cell"><span className="stat-value">{jobs.length}</span><span className="stat-label">Matches</span></div>
            <div className="stat-cell"><span className="stat-value">{recommended.length}</span><span className="stat-label">Recommended</span></div>
            <div className="stat-cell"><span className="stat-value">{avgScore}</span><span className="stat-label">Avg Score</span></div>
            <div className="stat-cell"><span className="stat-value">{total}</span><span className="stat-label">Rejected</span></div>
          </div>
        )}
        {loading && <div className="hero-stats"><div className="stat-cell"><span className="stat-value">…</span><span className="stat-label">Loading</span></div></div>}
      </div>

      <div className="page-content">
        {/* source status */}
        {sourceStatus.length > 0 && (
          <div className="source-table" style={{ marginTop: 32 }}>
            <div className="source-table-title">Source Status</div>
            <table>
              <thead><tr>
                <th>Source</th><th>Status</th><th>Jobs</th><th>Mode</th><th>Warnings</th>
              </tr></thead>
              <tbody>
                {sourceStatus.map(s => (
                  <tr key={s.source}>
                    <td style={{ color: "var(--text)", fontWeight: 600 }}>{s.source}</td>
                    <td><StatusDot status={s.status} /></td>
                    <td style={{ color: "var(--blue-bright)" }}>{s.jobsFetched}</td>
                    <td>{s.mode}</td>
                    <td style={{ fontSize: ".78rem" }}>{s.warnings.join("; ") || "—"}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        {/* top picks */}
        <div className="section-header" style={{ marginTop: 32 }}>
          <h2 className="section-title">Top Picks</h2>
          <Link to="/jobs" className="section-sub">Browse all →</Link>
        </div>

        {loading && <LoadingState />}
        {!loading && error && <ErrorState message={error} />}
        {!loading && !error && (
          <div className="job-grid">
            {jobs.slice(0, 6).map(job => <JobCard key={job.id} job={job} />)}
          </div>
        )}

        {/* rejected summary */}
        {Object.keys(rejectedSummary).length > 0 && (
          <>
            <div className="section-header" style={{ marginTop: 36 }}>
              <h2 className="section-title">Rejection Summary</h2>
            </div>
            <div className="dash-grid">
              {Object.entries(rejectedSummary).sort((a, b) => b[1] - a[1]).map(([reason, count]) => (
                <div className="dash-card" key={reason}>
                  <div className="dash-card-value">{count}</div>
                  <div className="dash-card-label">{reason}</div>
                </div>
              ))}
            </div>
          </>
        )}
      </div>
    </div>
  );
}

/* ═══════════════════════════════════════════════════════════════════════════
   PAGE: Browse
   ═══════════════════════════════════════════════════════════════════════════ */

type SortMode = "score-desc" | "posted-desc" | "salary-desc" | "salary-asc" | "distance-asc" | "title-asc";

const SORT_OPTIONS: Array<{ label: string; value: SortMode }> = [
  { label: "Best score",        value: "score-desc" },
  { label: "Newest first",      value: "posted-desc" },
  { label: "Salary: high→low",  value: "salary-desc" },
  { label: "Salary: low→high",  value: "salary-asc" },
  { label: "Nearest first",     value: "distance-asc" },
  { label: "Title A–Z",         value: "title-asc" },
];

type ConfigField = {
  key: string;
  label: string;
  placeholder: string;
  type?: "text" | "time" | "number" | "password";
  min?: number;
  step?: number;
};

const SEARCH_SETTINGS: ConfigField[] = [
  { key: "JOB_SEARCH_TIME_ZONE", label: "Time Zone", placeholder: "Europe/London" },
  { key: "JOB_SEARCH_RUN_AT", label: "Run At", placeholder: "10:00", type: "time" },
  { key: "JOB_SEARCH_POSTCODE", label: "Postcode", placeholder: "MK4 4QG" },
  { key: "JOB_SEARCH_RADIUS_MILES", label: "Radius Miles", placeholder: "50", type: "number", min: 1, step: 1 },
  { key: "JOB_SEARCH_POSTED_WITHIN_DAYS", label: "Posted Within Days", placeholder: "7", type: "number", min: 1, step: 1 },
  { key: "JOB_SEARCH_MIN_PERMANENT_SALARY_GBP", label: "Min Permanent Salary GBP", placeholder: "75000", type: "number", min: 0, step: 1000 },
  { key: "JOB_SEARCH_MIN_CONTRACT_DAY_RATE_GBP", label: "Min Contract Day Rate GBP", placeholder: "400", type: "number", min: 0, step: 25 },
  { key: "JOB_SEARCH_MIN_CONTRACT_MONTHS", label: "Min Contract Months", placeholder: "6", type: "number", min: 1, step: 1 },
];

function errorMessage(error: unknown): string {
  return error instanceof Error ? error.message : "Unexpected error";
}

function isAllowedCvFile(file: File): boolean {
  const name = file.name.toLowerCase();
  return name.endsWith(".pdf") || name.endsWith(".docx") || name.endsWith(".md");
}

function uniqueCvName(fileName: string, existing: CvFile[]): string {
  const dot = fileName.lastIndexOf(".");
  const base = dot > 0 ? fileName.slice(0, dot) : fileName;
  const ext = dot > 0 ? fileName.slice(dot) : "";
  const names = new Set(existing.map(file => file.name.toLowerCase()));
  let candidate = fileName;
  let index = 2;
  while (names.has(candidate.toLowerCase())) {
    candidate = `${base}-${index}${ext}`;
    index += 1;
  }
  return candidate;
}

function formatBytes(value: number): string {
  if (value < 1024) return `${value} B`;
  if (value < 1024 * 1024) return `${Math.round(value / 1024)} KB`;
  return `${(value / 1024 / 1024).toFixed(1)} MB`;
}

function BrowsePage(): ReactElement {
  const { jobs, loading, error } = useAppData();

  const [recFilter, setRecFilter]    = useState<"All" | "Recommended" | "Watchlist">("All");
  const [workFilter, setWorkFilter]  = useState<"All" | "Remote" | "Hybrid" | "Office">("All");
  const [empFilter, setEmpFilter]    = useState<"All" | "Permanent" | "Contract">("All");
  const [locFilter, setLocFilter]    = useState("");
  const [minComp, setMinComp]        = useState(0);
  const [maxMiles, setMaxMiles]      = useState(50);
  const [sort, setSort]              = useState<SortMode>("score-desc");
  const [page, setPage]              = useState(1);
  const PAGE_SIZE = 9;

  const filtered = useMemo(() => {
    const f = jobs.filter(j => {
      if (recFilter === "Recommended" && !j.recommended) return false;
      if (recFilter === "Watchlist"   &&  j.recommended) return false;
      if (workFilter !== "All" && j.workMode !== workFilter) return false;
      if (empFilter  !== "All" && j.employmentType !== empFilter) return false;
      if (locFilter && !j.location.toLowerCase().includes(locFilter.toLowerCase()) && !j.company.toLowerCase().includes(locFilter.toLowerCase())) return false;
      if (compFloor(j) < minComp) return false;
      if (j.distanceMiles > maxMiles && j.workMode !== "Remote") return false;
      return true;
    });
    return [...f].sort((a, b) => {
      switch (sort) {
        case "posted-desc":  return b.postedDate.localeCompare(a.postedDate);
        case "salary-desc":  return compFloor(b) - compFloor(a);
        case "salary-asc":   return compFloor(a) - compFloor(b);
        case "distance-asc": return a.distanceMiles - b.distanceMiles;
        case "title-asc":    return a.title.localeCompare(b.title);
        default:             return b.score - a.score;
      }
    });
  }, [jobs, recFilter, workFilter, empFilter, locFilter, minComp, maxMiles, sort]);

  const pageCount = Math.max(1, Math.ceil(filtered.length / PAGE_SIZE));
  const safePage  = Math.min(page, pageCount);
  const visible   = filtered.slice((safePage - 1) * PAGE_SIZE, safePage * PAGE_SIZE);

  function reset() {
    setRecFilter("All"); setWorkFilter("All"); setEmpFilter("All");
    setLocFilter(""); setMinComp(0); setMaxMiles(50); setPage(1);
  }

  return (
    <div className="page-content">
      <div className="browse-layout">
        {/* sidebar */}
        <aside className="filter-sidebar">
          <div className="sidebar-title">⚙ Filters</div>

          <div className="filter-section">
            <span className="filter-label">Location / Company</span>
            <input className="filter-input" placeholder="e.g. Milton Keynes" value={locFilter}
              onChange={e => { setLocFilter(e.target.value); setPage(1); }} />
          </div>

          <div className="filter-section">
            <span className="filter-label">Min Salary / Day Rate</span>
            <input className="filter-input" type="number" min={0} step={5000} value={minComp}
              onChange={e => { setMinComp(Number(e.target.value)); setPage(1); }} />
          </div>

          <div className="filter-section">
            <span className="filter-label">Within {maxMiles} miles</span>
            <input className="filter-range" type="range" min={5} max={50} value={maxMiles}
              onChange={e => { setMaxMiles(Number(e.target.value)); setPage(1); }} />
          </div>

          <div className="filter-section">
            <span className="filter-label">Match Quality</span>
            <div className="seg-group">
              {(["All", "Recommended", "Watchlist"] as const).map(v => (
                <button key={v} className={"seg-btn" + (recFilter === v ? " active" : "")}
                  onClick={() => { setRecFilter(v); setPage(1); }}>{v}</button>
              ))}
            </div>
          </div>

          <div className="filter-section">
            <span className="filter-label">Work Mode</span>
            <div className="seg-group">
              {(["All", "Remote", "Hybrid", "Office"] as const).map(v => (
                <button key={v} className={"seg-btn" + (workFilter === v ? " active" : "")}
                  onClick={() => { setWorkFilter(v); setPage(1); }}>{v}</button>
              ))}
            </div>
          </div>

          <div className="filter-section">
            <span className="filter-label">Employment</span>
            <div className="seg-group">
              {(["All", "Permanent", "Contract"] as const).map(v => (
                <button key={v} className={"seg-btn" + (empFilter === v ? " active" : "")}
                  onClick={() => { setEmpFilter(v); setPage(1); }}>{v}</button>
              ))}
            </div>
          </div>

          <button className="reset-btn" onClick={reset}>Reset filters</button>
        </aside>

        {/* results */}
        <div className="results-col">
          <div className="results-toolbar">
            <span className="result-count">
              <strong>{filtered.length}</strong> of {jobs.length} roles
            </span>
            <div className="toolbar-sort">
              <span className="sort-label">Sort:</span>
              <select className="sort-select" value={sort}
                onChange={e => { setSort(e.target.value as SortMode); setPage(1); }}>
                {SORT_OPTIONS.map(o => <option key={o.value} value={o.value}>{o.label}</option>)}
              </select>
            </div>
          </div>

          {loading && <LoadingState />}
          {!loading && error && <ErrorState message={error} />}
          {!loading && !error && filtered.length === 0 && (
            <div className="state-box"><span className="icon">🔍</span>No roles match your filters.</div>
          )}
          {!loading && !error && filtered.length > 0 && (
            <>
              <div className="job-grid">
                {visible.map(job => <JobCard key={job.id} job={job} />)}
              </div>
              <Pagination page={safePage} pageCount={pageCount} onChange={setPage} />
            </>
          )}
        </div>
      </div>
    </div>
  );
}

/* ═══════════════════════════════════════════════════════════════════════════
   PAGE: Job Detail
   ═══════════════════════════════════════════════════════════════════════════ */

function DetailPage(): ReactElement {
  const { source, sourceJobId } = useParams<{ source: string; sourceJobId: string }>();
  const { jobs } = useAppData();

  const [detail, setDetail]   = useState<JobDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError]     = useState<string | null>(null);

  // Try to seed from cached list first
  const cached = useMemo(() =>
    jobs.find(j => j.source === source && j.sourceJobId === sourceJobId) ?? null,
    [jobs, source, sourceJobId]
  );

  useEffect(() => {
    if (!source || !sourceJobId) return;
    setLoading(true);
    fetchJobDetail(source, sourceJobId).then(res => {
      if (res.status === "ok") {
        // Merge cached score/reasons into the detail record
        const enriched: JobDetail = cached
          ? { ...res.job, score: cached.score, recommended: cached.recommended, reasons: cached.reasons, risks: cached.risks }
          : res.job;
        setDetail(enriched);
        setError(null);
      } else if (cached) {
        // Fall back to cached list data with empty description
        setDetail({ ...cached, description: "Full description not available. Open the original advert for details." });
        setError(null);
      } else {
        setError(res.message);
      }
      setLoading(false);
    });
  }, [source, sourceJobId, cached]);

  const job = detail ?? (cached ? { ...cached, description: "" } : null);

  return (
    <div className="page-content">
      <div className="detail-page">
        <Link to="/jobs" className="detail-back">← Back to Browse</Link>

        {loading && !job && <LoadingState />}
        {!loading && error && !job && <ErrorState message={error} />}

        {job && (
          <>
            {/* hero block */}
            <div className="detail-hero">
              <div>
                <div style={{ display: "flex", gap: 10, alignItems: "center", marginBottom: 12, flexWrap: "wrap" }}>
                  <span className="source-chip">{job.source}</span>
                  {job.recommended && <span className="rec-badge">★ Recommended</span>}
                </div>
                <h1 className="detail-title">{job.title}</h1>
                <div className="detail-company">{job.company}</div>
                <div className="detail-tags">
                  <span className="dtag">{job.location}</span>
                  {job.distanceMiles > 0 && <span className="dtag">{job.distanceMiles} miles</span>}
                  <span className="dtag">{job.workMode}</span>
                  <span className="dtag">{job.employmentType}</span>
                  <span className="dtag comp">{formatComp(job)}</span>
                  {job.contractMonths && <span className="dtag green">{job.contractMonths} months</span>}
                  <span className="dtag">Posted {fmtDate(job.postedDate)}</span>
                </div>
              </div>

              {job.score > 0 && (
                <div className="detail-score-block">
                  <div
                    className={`big-score-ring ${scoreClass(job.score)}`}
                    style={{ "--pct": job.score } as React.CSSProperties}
                  >
                    <span className="big-score-num">{job.score}</span>
                  </div>
                  <span className="score-label">CV Match</span>
                  {job.recommended && <span className="rec-badge" style={{ fontSize: ".7rem" }}>★ Rec</span>}
                </div>
              )}
            </div>

            {/* detail grid */}
            <div className="detail-grid">
              <div style={{ display: "flex", flexDirection: "column", gap: 20 }}>
                <div className="detail-card">
                  <h3>Job Description</h3>
                  <p className="description-text">
                    {job.description || "Full description not available — open the original advert below."}
                  </p>
                </div>

                {job.reasons.length > 0 && (
                  <div className="detail-card">
                    <h3>Why it matches your CV</h3>
                    <div className="insight-list">
                      {job.reasons.map(r => (
                        <div key={r} className="insight-item match">
                          <span className="insight-icon">✓</span><span>{r}</span>
                        </div>
                      ))}
                    </div>
                  </div>
                )}

                {job.risks.length > 0 && (
                  <div className="detail-card">
                    <h3>Risks to review</h3>
                    <div className="insight-list">
                      {job.risks.map(r => (
                        <div key={r} className="insight-item risk">
                          <span className="insight-icon">⚠</span><span>{r}</span>
                        </div>
                      ))}
                    </div>
                  </div>
                )}
              </div>

              {/* right sidebar */}
              <div style={{ display: "flex", flexDirection: "column", gap: 16 }}>
                <div className="detail-card">
                  <h3>Apply</h3>
                  {job.url ? (
                    <a className="apply-btn" href={job.url} target="_blank" rel="noreferrer">
                      Open Original Advert ↗
                    </a>
                  ) : (
                    <button className="apply-btn" disabled>No advert URL</button>
                  )}
                </div>

                <div className="detail-card">
                  <h3>Role Details</h3>
                  <div style={{ display: "flex", flexDirection: "column", gap: 10, fontSize: ".85rem" }}>
                    <Detail label="Source"     value={job.source} />
                    <Detail label="Location"   value={job.location} />
                    <Detail label="Work Mode"  value={job.workMode} />
                    <Detail label="Type"       value={job.employmentType} />
                    <Detail label="Pay"        value={formatComp(job)} highlight />
                    {job.contractMonths && <Detail label="Duration" value={`${job.contractMonths} months`} />}
                    <Detail label="Posted"     value={fmtDate(job.postedDate)} />
                    <Detail label="Source ID"  value={job.sourceJobId} />
                  </div>
                </div>
              </div>
            </div>
          </>
        )}
      </div>
    </div>
  );
}

function Detail({ label, value, highlight }: { label: string; value: string; highlight?: boolean }): ReactElement {
  return (
    <div style={{ display: "flex", justifyContent: "space-between", gap: 8, borderBottom: "1px solid var(--border-soft)", paddingBottom: 8 }}>
      <span style={{ color: "var(--text-dim)" }}>{label}</span>
      <span style={{ color: highlight ? "var(--blue-bright)" : "var(--text)", fontWeight: highlight ? 700 : 400, textAlign: "right" }}>{value}</span>
    </div>
  );
}

/* ═══════════════════════════════════════════════════════════════════════════
   PAGE: Settings
   ═══════════════════════════════════════════════════════════════════════════ */

function SettingsPage(): ReactElement {
  const navigate = useNavigate();
  const [config, setConfig]   = useState<Record<string, string>>({});
  const [configuredSecrets, setConfiguredSecrets] = useState<Set<string>>(new Set());
  const [cvFiles, setCvFiles] = useState<CvFile[]>([]);
  const [cvFile, setCvFile] = useState<File | null>(null);
  const [cvMode, setCvMode] = useState<"replace" | "rename">("rename");
  const [replaceName, setReplaceName] = useState("");
  const [newCvName, setNewCvName] = useState("");
  const [uploadingCv, setUploadingCv] = useState(false);
  const [saving, setSaving]   = useState(false);
  const [toast, setToast]     = useState<{ msg: string; ok: boolean } | null>(null);

  useEffect(() => {
    Promise.all([getConfig(), fetchCvs()])
      .then(([values, cvs]) => {
        setConfig(values);
        setCvFiles(cvs);
        setReplaceName(cvs[0]?.name ?? "");
        const configured = (values["__configuredSecretKeys"] ?? "")
          .split(",")
          .map(value => value.trim())
          .filter(Boolean);
        setConfiguredSecrets(new Set(configured));
      })
      .catch(error => setToast({ msg: errorMessage(error), ok: false }));
  }, []);

  async function handleSave(e: React.FormEvent) {
    e.preventDefault();
    setSaving(true);
    try {
      await saveConfig(config);
      setToast({ msg: "Configuration saved.", ok: true });
      if (config["REED_API_KEY"]) {
        setConfiguredSecrets(prev => new Set(prev).add("REED_API_KEY"));
        setConfig(prev => ({ ...prev, REED_API_KEY: "" }));
      }
    } catch (error) {
      setToast({ msg: errorMessage(error), ok: false });
    }
    setSaving(false);
  }

  async function reloadCvs() {
    const cvs = await fetchCvs();
    setCvFiles(cvs);
    setReplaceName(cvs[0]?.name ?? "");
  }

  function handleCvFileChange(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0] ?? null;
    if (file && !isAllowedCvFile(file)) {
      e.target.value = "";
      setCvFile(null);
      setToast({ msg: "Only .pdf, .docx, and .md files are allowed.", ok: false });
      return;
    }

    setCvFile(file);
    setToast(null);
    if (file) {
      const sameName = cvFiles.find(cv => cv.name.toLowerCase() === file.name.toLowerCase());
      setCvMode(cvFiles.length > 0 ? "replace" : "rename");
      setReplaceName(sameName?.name ?? cvFiles[0]?.name ?? "");
      setNewCvName(uniqueCvName(file.name, cvFiles));
    }
  }

  async function handleCvUpload() {
    if (!cvFile) {
      setToast({ msg: "Select a CV file first.", ok: false });
      return;
    }

    const targetName = cvFiles.length > 0 && cvMode === "replace" ? replaceName : newCvName;
    if (!targetName.trim()) {
      setToast({ msg: "Enter a CV file name.", ok: false });
      return;
    }

    setUploadingCv(true);
    try {
      await uploadCv(cvFile, cvFiles.length > 0 ? cvMode : "rename", targetName.trim());
      await reloadCvs();
      setCvFile(null);
      setNewCvName("");
      setToast({ msg: "CV uploaded.", ok: true });
    } catch (error) {
      setToast({ msg: errorMessage(error), ok: false });
    }
    setUploadingCv(false);
  }

  const set = (k: string) => (e: React.ChangeEvent<HTMLInputElement | HTMLTextAreaElement>) => {
    setConfig(prev => ({ ...prev, [k]: e.target.value }));
    setToast(null);
  };

  return (
    <div className="page-content">
      <div className="settings-page">
        <div className="settings-hero">
          <span className="settings-eyebrow">Automation Control</span>
          <h1>Settings</h1>
          <p>Tune the daily search radius, pay thresholds, source credentials, and inbox integrations from one secure console.</p>
        </div>

        <form onSubmit={handleSave}>
          <div className="settings-section">
            <div className="settings-section-title">Job Profile</div>
            <div className="settings-body">
              <div className="form-field">
                <label>Desired Designations</label>
                <textarea
                  rows={3}
                  placeholder="Senior Software Engineer, Lead Developer, Principal Engineer"
                  value={config["JOB_SEARCH_DESIRED_DESIGNATION"] ?? ""}
                  onChange={set("JOB_SEARCH_DESIRED_DESIGNATION")}
                />
                <span className="field-hint">Comma-separated job titles used as search keywords.</span>
              </div>
              <div className="form-field">
                <label>Skills</label>
                <textarea
                  rows={3}
                  placeholder="C#, ASP.NET Core, React, TypeScript, AWS, Docker"
                  value={config["JOB_SEARCH_SKILLS"] ?? ""}
                  onChange={set("JOB_SEARCH_SKILLS")}
                />
                <span className="field-hint">Comma-separated skills used to score CV match against job listings.</span>
              </div>
            </div>
          </div>

          <div className="settings-section">
            <div className="settings-section-title">Search Criteria</div>
            <div className="settings-grid">
              {SEARCH_SETTINGS.map(field => (
                <div className="form-field" key={field.key}>
                  <label>{field.label}</label>
                  <input
                    type={field.type ?? "text"}
                    min={field.min}
                    step={field.step}
                    placeholder={field.placeholder}
                    value={config[field.key] ?? ""}
                    onChange={set(field.key)}
                  />
                </div>
              ))}
            </div>
          </div>

          <div className="settings-section">
            <div className="settings-section-title">Reed API</div>
            <div className="settings-body">
              <div className="form-field">
                <label>API Key</label>
                <input
                  type="password"
                  placeholder={configuredSecrets.has("REED_API_KEY") ? "Saved securely. Enter a new key to replace it." : "Paste Reed API key"}
                  value={config["REED_API_KEY"] ?? ""}
                  onChange={set("REED_API_KEY")}
                />
                {configuredSecrets.has("REED_API_KEY") && <span className="secret-status">Current key is stored encrypted in the database.</span>}
              </div>
            </div>
          </div>

          <div className="settings-section">
            <div className="settings-section-title">CV Upload</div>
            <div className="settings-body">
              <div className="cv-file-list">
                {cvFiles.length === 0 && <span className="cv-empty">No CVs saved yet.</span>}
                {cvFiles.map(file => (
                  <div className="cv-file-row" key={file.name}>
                    <span>{file.name}</span>
                    <small>{formatBytes(file.sizeBytes)} · {fmtDate(file.updatedAtUtc.slice(0, 10))}</small>
                  </div>
                ))}
              </div>

              <div className="form-field">
                <label>Upload CV</label>
                <input accept=".pdf,.docx,.md" type="file" onChange={handleCvFileChange} />
              </div>

              {cvFile && cvFiles.length > 0 && (
                <div className="cv-upload-options">
                  <label className="cv-option">
                    <input
                      checked={cvMode === "replace"}
                      name="cv-upload-mode"
                      onChange={() => setCvMode("replace")}
                      type="radio"
                    />
                    Replace Existing
                  </label>
                  <label className="cv-option">
                    <input
                      checked={cvMode === "rename"}
                      name="cv-upload-mode"
                      onChange={() => setCvMode("rename")}
                      type="radio"
                    />
                    Add With New Name
                  </label>
                </div>
              )}

              {cvFile && cvFiles.length > 0 && cvMode === "replace" && (
                <div className="form-field">
                  <label>Replace</label>
                  <select className="settings-select" value={replaceName} onChange={e => setReplaceName(e.target.value)}>
                    {cvFiles.map(file => <option key={file.name} value={file.name}>{file.name}</option>)}
                  </select>
                </div>
              )}

              {cvFile && (cvFiles.length === 0 || cvMode === "rename") && (
                <div className="form-field">
                  <label>Save As</label>
                  <input value={newCvName} onChange={e => setNewCvName(e.target.value)} />
                </div>
              )}

              <button className="save-btn compact" disabled={!cvFile || uploadingCv} onClick={handleCvUpload} type="button">
                {uploadingCv ? "Uploading…" : "Upload CV"}
              </button>
            </div>
          </div>

          <div className="settings-section">
            <div className="settings-section-title">Slack Integration</div>
            <div className="settings-body">
              <div className="form-field">
                <label>Webhook URL</label>
                <input placeholder="https://hooks.slack.com/services/..." value={config["SLACK_WEBHOOK_URL"] ?? ""}
                  onChange={set("SLACK_WEBHOOK_URL")} />
              </div>
            </div>
          </div>

          <div className="settings-section">
            <div className="settings-section-title">Gmail Integration</div>
            <div className="settings-body">
              <div className="form-field">
                <label>Service Account Credentials JSON</label>
                <textarea rows={6} placeholder='{"type": "service_account", ...}'
                  value={config["GMAIL_CREDENTIALS_JSON"] ?? ""} onChange={set("GMAIL_CREDENTIALS_JSON")} />
              </div>
              <div className="form-field">
                <label>Mailbox User Email</label>
                <input placeholder="you@your-domain.com" value={config["GMAIL_USER_EMAIL"] ?? ""}
                  onChange={set("GMAIL_USER_EMAIL")} />
              </div>
              <div className="form-field">
                <label>Gmail Search Query</label>
                <input placeholder="label:job-alerts is:unread" value={config["GMAIL_SEARCH_QUERY"] ?? ""}
                  onChange={set("GMAIL_SEARCH_QUERY")} />
              </div>
            </div>
          </div>

          <div className="settings-section">
            <div className="settings-section-title">Indeed Alert Emails</div>
            <div className="settings-body">
              <div className="form-field">
                <label>Indeed Gmail Search Query</label>
                <input placeholder="from:jobalerts-noreply@indeed.com is:unread"
                  value={config["INDEED_GMAIL_SEARCH_QUERY"] ?? ""} onChange={set("INDEED_GMAIL_SEARCH_QUERY")} />
              </div>
            </div>
          </div>

          <div className="settings-actions">
            <button className="save-btn" type="submit" disabled={saving}>
              {saving ? "Saving…" : "Save Configuration"}
            </button>
            <button className="cancel-btn" type="button" onClick={() => navigate(-1)}>Cancel</button>
            {toast && <span className={`toast ${toast.ok ? "ok" : "err"}`}>{toast.msg}</span>}
          </div>
        </form>
      </div>
    </div>
  );
}

/* ═══════════════════════════════════════════════════════════════════════════
   SHARED COMPONENTS
   ═══════════════════════════════════════════════════════════════════════════ */

function JobCard({ job }: { job: JobResult }): ReactElement {
  const pct = job.score;
  const cls = scoreClass(job.score);
  return (
    <Link
      to={`/jobs/${encodeURIComponent(job.source)}/${encodeURIComponent(job.sourceJobId)}`}
      className={`job-card${job.recommended ? " recommended" : ""}`}
    >
      <div className="card-top">
        <span className="source-chip">{job.source}</span>
        <div className={`score-ring ${cls}`} style={{ "--pct": pct } as React.CSSProperties}>
          <span className="score-num">{pct}</span>
        </div>
      </div>

      <div>
        <div className="card-title">{job.title}</div>
        <div className="card-company">{job.company}</div>
      </div>

      {job.recommended && <span className="rec-badge">★ Recommended</span>}

      <div className="card-meta">
        <span className="meta-tag">{job.location}</span>
        <span className="meta-tag">{job.workMode}</span>
        <span className="meta-tag">{job.employmentType}</span>
        <span className="meta-tag comp">{formatComp(job)}</span>
        <span className="meta-tag">Posted {fmtDate(job.postedDate)}</span>
      </div>

      {job.reasons.length > 0 && (
        <div className="card-reasons">
          <ul>
            {job.reasons.slice(0, 3).map(r => <li key={r}>{r}</li>)}
          </ul>
        </div>
      )}

      <span className="card-cta">View details →</span>
    </Link>
  );
}

function StatusDot({ status }: { status: string }): ReactElement {
  const cls = status === "Succeeded" ? "ok" : status === "PolicyBlocked" ? "err" : status === "Skipped" ? "skip" : "warn";
  return <span className={`status-dot ${cls}`}>{status}</span>;
}

function Pagination({ page, pageCount, onChange }: { page: number; pageCount: number; onChange: (p: number) => void }): ReactElement {
  const pages = Array.from({ length: pageCount }, (_, i) => i + 1)
    .filter(p => p === 1 || p === pageCount || Math.abs(p - page) <= 2);

  return (
    <nav className="pagination" aria-label="Pagination">
      <button className="page-btn" disabled={page === 1} onClick={() => onChange(page - 1)}>←</button>
      {pages.map((p, i) => (
        <>
          {i > 0 && pages[i - 1] !== p - 1 && <span key={`gap-${p}`} className="page-btn" style={{ cursor: "default" }}>…</span>}
          <button key={p} className={`page-btn${p === page ? " current" : ""}`} onClick={() => onChange(p)}>{p}</button>
        </>
      ))}
      <button className="page-btn" disabled={page === pageCount} onClick={() => onChange(page + 1)}>→</button>
    </nav>
  );
}

function LoadingState(): ReactElement {
  return <div className="state-box"><div className="spinner" /><span>Loading job matches…</span></div>;
}

function ErrorState({ message }: { message: string }): ReactElement {
  return <div className="state-box error"><span className="icon">⚠</span><span>{message}</span></div>;
}
