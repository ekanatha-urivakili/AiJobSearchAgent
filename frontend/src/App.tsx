import { useEffect, useMemo, useState } from "react";
import { fetchMatches, getConfig, saveConfig } from "./api";
import type { EmploymentType, JobResult, WorkMode } from "./types";
import type { ReactElement } from "react";

type RecommendationFilter = "All" | "Recommended" | "Watchlist";
type SortMode =
  | "score-desc"
  | "posted-desc"
  | "salary-desc"
  | "salary-asc"
  | "distance-asc"
  | "distance-desc"
  | "title-asc";

const recommendationFilters: RecommendationFilter[] = ["All", "Recommended", "Watchlist"];
const workModes: Array<WorkMode | "All"> = ["All", "Remote", "Hybrid", "Office"];
const employmentTypes: Array<EmploymentType | "All"> = ["All", "Permanent", "Contract"];
const pageSizeOptions = [3, 6, 9];
const sortOptions: Array<{ label: string; value: SortMode }> = [
  { label: "Best score", value: "score-desc" },
  { label: "Newest", value: "posted-desc" },
  { label: "Salary high to low", value: "salary-desc" },
  { label: "Salary low to high", value: "salary-asc" },
  { label: "Miles near to far", value: "distance-asc" },
  { label: "Miles far to near", value: "distance-desc" },
  { label: "Title A-Z", value: "title-asc" },
];

const currencyFormatter = new Intl.NumberFormat("en-GB", {
  style: "currency",
  currency: "GBP",
  maximumFractionDigits: 0,
});

function formatCompensation(job: JobResult): string {
  if (job.employmentType === "Contract") {
    const min = job.dayRateMinGbp ? currencyFormatter.format(job.dayRateMinGbp) : "Rate TBC";
    const max = job.dayRateMaxGbp ? currencyFormatter.format(job.dayRateMaxGbp) : undefined;
    const range = max ? `${min}-${max}/day` : `${min}/day`;
    return job.contractMonths ? `${range}, ${job.contractMonths} months` : range;
  }

  const min = job.salaryMinGbp ? currencyFormatter.format(job.salaryMinGbp) : "Salary TBC";
  const max = job.salaryMaxGbp ? currencyFormatter.format(job.salaryMaxGbp) : undefined;
  return max ? `${min}-${max}` : min;
}

function formatDate(value: string): string {
  return new Intl.DateTimeFormat("en-GB", {
    day: "2-digit",
    month: "short",
    year: "numeric",
  }).format(new Date(`${value}T00:00:00Z`));
}

function getCompensationFloor(job: JobResult): number {
  return job.employmentType === "Contract" ? job.dayRateMinGbp ?? 0 : job.salaryMinGbp ?? 0;
}

function isExactAdvertUrl(value: string): boolean {
  const genericPaths = ["jobs/senior-fullstack-engineer-jobs", "jobs/senior-software-developer-jobs", "Job-Search"];
  return value.length > 0 && !genericPaths.some((path) => value.includes(path));
}

function sortJobs(jobs: JobResult[], sortMode: SortMode): JobResult[] {
  return [...jobs].sort((first, second) => {
    switch (sortMode) {
      case "posted-desc":
        return second.postedDate.localeCompare(first.postedDate);
      case "salary-desc":
        return getCompensationFloor(second) - getCompensationFloor(first);
      case "salary-asc":
        return getCompensationFloor(first) - getCompensationFloor(second);
      case "distance-asc":
        return first.distanceMiles - second.distanceMiles;
      case "distance-desc":
        return second.distanceMiles - first.distanceMiles;
      case "title-asc":
        return first.title.localeCompare(second.title);
      case "score-desc":
        return second.score - first.score;
    }
  });
}

function App(): ReactElement {
  const [view, setView] = useState<"results" | "settings">("results");
  const [recommendationFilter, setRecommendationFilter] = useState<RecommendationFilter>("All");
  const [workModeFilter, setWorkModeFilter] = useState<WorkMode | "All">("All");
  const [employmentFilter, setEmploymentFilter] = useState<EmploymentType | "All">("All");
  const [locationFilter, setLocationFilter] = useState("");
  const [minimumCompensation, setMinimumCompensation] = useState(0);
  const [maximumMiles, setMaximumMiles] = useState(50);
  const [sortMode, setSortMode] = useState<SortMode>("score-desc");
  const [pageSize, setPageSize] = useState(3);
  const [currentPage, setCurrentPage] = useState(1);

  const [jobResults, setJobResults] = useState<JobResult[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let mounted = true;
    setLoading(true);
    fetchMatches().then((result) => {
      if (!mounted) return;
      if (result.status === "ok") {
        setJobResults(result.jobs);
        setError(null);
      } else {
        setError(result.message);
      }
      setLoading(false);
    });
    return () => {
      mounted = false;
    };
  }, []);

  const filteredJobs = useMemo(() => {
    const filtered = jobResults.filter((job) => {
      const matchesRecommendation =
        recommendationFilter === "All" ||
        (recommendationFilter === "Recommended" && job.recommended) ||
        (recommendationFilter === "Watchlist" && !job.recommended);
      const matchesWorkMode = workModeFilter === "All" || job.workMode === workModeFilter;
      const matchesEmployment = employmentFilter === "All" || job.employmentType === employmentFilter;
      const normalizedLocation = locationFilter.trim().toLowerCase();
      const matchesLocation =
        normalizedLocation.length === 0 ||
        job.location.toLowerCase().includes(normalizedLocation) ||
        job.company.toLowerCase().includes(normalizedLocation);
      const matchesCompensation = getCompensationFloor(job) >= minimumCompensation;
      const matchesMiles = job.distanceMiles <= maximumMiles;

      return (
        matchesRecommendation &&
        matchesWorkMode &&
        matchesEmployment &&
        matchesLocation &&
        matchesCompensation &&
        matchesMiles
      );
    });

    return sortJobs(filtered, sortMode);
  }, [employmentFilter, locationFilter, maximumMiles, minimumCompensation, recommendationFilter, sortMode, workModeFilter]);

  const pageCount = Math.max(1, Math.ceil(filteredJobs.length / pageSize));
  const safeCurrentPage = Math.min(currentPage, pageCount);
  const pageStart = (safeCurrentPage - 1) * pageSize;
  const visibleJobs = filteredJobs.slice(pageStart, pageStart + pageSize);

  const recommendedCount = jobResults.filter((job) => job.recommended).length;
  const averageScore = jobResults.length > 0 ? Math.round(jobResults.reduce((total, job) => total + job.score, 0) / jobResults.length) : 0;

  function updateFilter(action: () => void): void {
    action();
    setCurrentPage(1);
  }

  return (
    <main className="app-shell">
      <section className="summary-band" aria-labelledby="page-title">
        <div>
          <p className="eyebrow">AI Job Search Agent</p>
          <h1 id="page-title">Matched senior UK software roles</h1>
          <p className="summary-copy">
            Review scored job matches, scan risks, and open the original advert when you are ready to apply.
          </p>
        </div>
        <div className="summary-actions">
          <button className="settings-toggle" onClick={() => setView(view === "results" ? "settings" : "results")}>
            {view === "results" ? "Settings" : "Back to Results"}
          </button>
        </div>
        <div className="summary-metrics" aria-label="Search summary">
          <Metric label="Results" value={jobResults.length.toString()} />
          <Metric label="Recommended" value={recommendedCount.toString()} />
          <Metric label="Avg score" value={`${averageScore}`} />
        </div>
      </section>

      {view === "settings" ? (
        <Settings onClose={() => setView("results")} />
      ) : (
        <>
          <section className="toolbar" aria-label="Filters">
        <label className="field-control">
          <span>Location</span>
          <input
            onChange={(event) => updateFilter(() => setLocationFilter(event.target.value))}
            placeholder="Milton Keynes, Oxford, remote"
            type="search"
            value={locationFilter}
          />
        </label>
        <label className="field-control">
          <span>Minimum salary or day rate</span>
          <input
            min="0"
            onChange={(event) => updateFilter(() => setMinimumCompensation(Number(event.target.value)))}
            step="25"
            type="number"
            value={minimumCompensation}
          />
        </label>
        <label className="range-control">
          <span>Within {maximumMiles} miles</span>
          <input
            max="50"
            min="0"
            onChange={(event) => updateFilter(() => setMaximumMiles(Number(event.target.value)))}
            type="range"
            value={maximumMiles}
          />
        </label>
        <SegmentedControl
          label="Recommendation"
          options={recommendationFilters}
          value={recommendationFilter}
          onChange={(value) => updateFilter(() => setRecommendationFilter(value))}
        />
        <SegmentedControl
          label="Work mode"
          options={workModes}
          value={workModeFilter}
          onChange={(value) => updateFilter(() => setWorkModeFilter(value))}
        />
        <SegmentedControl
          label="Employment"
          options={employmentTypes}
          value={employmentFilter}
          onChange={(value) => updateFilter(() => setEmploymentFilter(value))}
        />
        <label className="field-control compact">
          <span>Sort by</span>
          <select onChange={(event) => updateFilter(() => setSortMode(event.target.value as SortMode))} value={sortMode}>
            {sortOptions.map((option) => (
              <option key={option.value} value={option.value}>
                {option.label}
              </option>
            ))}
          </select>
        </label>
        <label className="field-control compact">
          <span>Per page</span>
          <select onChange={(event) => updateFilter(() => setPageSize(Number(event.target.value)))} value={pageSize}>
            {pageSizeOptions.map((option) => (
              <option key={option} value={option}>
                {option}
              </option>
            ))}
          </select>
        </label>
      </section>

      <section className="results-layout" aria-label="Job results">
        {loading && <p className="status-message">Loading job matches…</p>}
        {!loading && error && <p className="status-message error">Failed to load jobs: {error}</p>}
        {!loading && !error && (
          <>
            <div className="results-header">
              <div className="result-count">
                <strong>{filteredJobs.length}</strong> matching roles
                <span>
                  Page {safeCurrentPage} of {pageCount}
                </span>
              </div>
              <Pagination currentPage={safeCurrentPage} pageCount={pageCount} onPageChange={setCurrentPage} />
            </div>
            <div className="job-grid">
              {visibleJobs.map((job) => (
                <JobCard key={job.id} job={job} />
              ))}
            </div>
            <Pagination currentPage={safeCurrentPage} pageCount={pageCount} onPageChange={setCurrentPage} />
          </>
        )}
      </section>
        </>
      )}
    </main>
  );
}

function Settings({ onClose }: { onClose: () => void }): ReactElement {
  const [config, setConfig] = useState<Record<string, string>>({});
  const [saving, setSaving] = useState(false);
  const [message, setMessage] = useState("");

  useEffect(() => {
    getConfig().then(setConfig).catch((err) => setMessage("Error loading config: " + err.message));
  }, []);

  const handleSave = async (e: React.FormEvent) => {
    e.preventDefault();
    setSaving(true);
    try {
      await saveConfig(config);
      setMessage("Config saved successfully!");
    } catch (err: any) {
      setMessage("Error saving config: " + err.message);
    }
    setSaving(false);
  };

  const handleChange = (key: string, value: string) => {
    setConfig((prev) => ({ ...prev, [key]: value }));
  };

  return (
    <div className="settings-panel">
      <div className="settings-header">
        <h2>Configuration Settings</h2>
        <p>Manage your Slack and Gmail credentials here. These are saved to your local .env file.</p>
      </div>
      <form className="settings-form" onSubmit={handleSave}>
        <div className="form-group">
          <label htmlFor="slack_webhook">Slack Webhook URL</label>
          <input
            id="slack_webhook"
            onChange={(e) => handleChange("SLACK_WEBHOOK_URL", e.target.value)}
            placeholder="https://hooks.slack.com/services/..."
            type="text"
            value={config["SLACK_WEBHOOK_URL"] || ""}
          />
        </div>
        <div className="form-group">
          <label htmlFor="gmail_creds">Gmail Credentials JSON</label>
          <textarea
            id="gmail_creds"
            onChange={(e) => handleChange("GMAIL_CREDENTIALS_JSON", e.target.value)}
            placeholder='{"type": "service_account", ...}'
            rows={8}
            value={config["GMAIL_CREDENTIALS_JSON"] || ""}
          />
        </div>
        <div className="form-group">
          <label htmlFor="gmail_query">Gmail Search Query</label>
          <input
            id="gmail_query"
            onChange={(e) => handleChange("GMAIL_SEARCH_QUERY", e.target.value)}
            placeholder="label:job-alerts is:unread"
            type="text"
            value={config["GMAIL_SEARCH_QUERY"] || ""}
          />
        </div>
        <div className="form-actions">
          <button className="save-btn" disabled={saving} type="submit">
            {saving ? "Saving..." : "Save Configuration"}
          </button>
          <button className="cancel-btn" onClick={onClose} type="button">
            Cancel
          </button>
          {message && <p className={message.includes("Error") ? "message error" : "message success"}>{message}</p>}
        </div>
      </form>
    </div>
  );
}

interface MetricProps {
  label: string;
  value: string;
}

function Metric({ label, value }: MetricProps): ReactElement {
  return (
    <div className="metric">
      <span>{label}</span>
      <strong>{value}</strong>
    </div>
  );
}

interface SegmentedControlProps<T extends string> {
  label: string;
  options: T[];
  value: T;
  onChange: (value: T) => void;
}

function SegmentedControl<T extends string>({
  label,
  options,
  value,
  onChange,
}: SegmentedControlProps<T>): ReactElement {
  return (
    <div className="filter-group">
      <span>{label}</span>
      <div className="segments">
        {options.map((option) => (
          <button
            className={option === value ? "segment active" : "segment"}
            key={option}
            onClick={() => onChange(option)}
            type="button"
          >
            {option}
          </button>
        ))}
      </div>
    </div>
  );
}

interface JobCardProps {
  job: JobResult;
}

function JobCard({ job }: JobCardProps): ReactElement {
  const canOpenAdvert = isExactAdvertUrl(job.url);

  return (
    <article className={job.recommended ? "job-card recommended" : "job-card"}>
      <div className="card-topline">
        <span className="source-pill">{job.source}</span>
        <span className="score">{job.score}</span>
      </div>

      <div>
        <h2>{job.title}</h2>
        <p className="company">{job.company}</p>
      </div>

      <div className="facts" aria-label={`${job.title} details`}>
        <span>{job.location}</span>
        <span>{job.distanceMiles === 0 ? "Remote" : `${job.distanceMiles} miles`}</span>
        <span>{job.workMode}</span>
        <span>{job.employmentType}</span>
        <span>{formatCompensation(job)}</span>
        <span>Posted {formatDate(job.postedDate)}</span>
      </div>

      <div className="reason-grid">
        <InsightList title="Why it matches" items={job.reasons} />
        <InsightList title="Risks" items={job.risks} />
      </div>

      {canOpenAdvert ? (
        <a className="apply-link" href={job.url} rel="noreferrer" target="_blank">
          Open exact job advert
          <span aria-hidden="true">↗</span>
        </a>
      ) : (
        <button className="apply-link unavailable" disabled type="button">
          Exact advert URL missing
        </button>
      )}
    </article>
  );
}

interface InsightListProps {
  title: string;
  items: string[];
}

function InsightList({ title, items }: InsightListProps): ReactElement {
  return (
    <div className="insight-list">
      <h3>{title}</h3>
      <ul>
        {items.map((item) => (
          <li key={item}>{item}</li>
        ))}
      </ul>
    </div>
  );
}

interface PaginationProps {
  currentPage: number;
  pageCount: number;
  onPageChange: (page: number) => void;
}

function Pagination({ currentPage, pageCount, onPageChange }: PaginationProps): ReactElement {
  return (
    <nav aria-label="Pagination" className="pagination">
      <button disabled={currentPage === 1} onClick={() => onPageChange(currentPage - 1)} type="button">
        Previous
      </button>
      <span>
        {currentPage} / {pageCount}
      </span>
      <button disabled={currentPage === pageCount} onClick={() => onPageChange(currentPage + 1)} type="button">
        Next
      </button>
    </nav>
  );
}

export { App };
