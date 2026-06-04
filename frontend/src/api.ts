import type { CvFile, EmploymentType, JobDetail, JobResult, SourceStatus, WorkMode } from "./types";

const BASE_URL = (import.meta.env.VITE_API_BASE_URL as string | undefined) ?? "http://localhost:5001";

/* ── raw DTO shapes ──────────────────────────────────────────────────────── */

interface JobMatchDto {
  source: string; sourceJobId: string; title: string; company: string;
  location: string; distanceMiles: number; employmentType: EmploymentType;
  workMode: WorkMode; salaryMin: number | null; salaryMax: number | null;
  dayRateMin: number | null; dayRateMax: number | null;
  contractMonths: number | null; postedDate: string; score: number;
  recommended: boolean; reasons: string[]; risks: string[]; url: string;
}

interface JobDto {
  source: string; sourceJobId: string; url: string; title: string;
  company: string; location: string; distanceMiles: number;
  employmentType: EmploymentType; workMode: WorkMode;
  salaryMin: number | null; salaryMax: number | null;
  dayRateMin: number | null; dayRateMax: number | null;
  contractMonths: number | null; postedDate: string; description: string;
}

interface SearchResponse {
  runId: string;
  reportResource: string;
  sourceStatus: SourceStatus[];
  matches: JobMatchDto[];
  rejectedSummary: Record<string, number>;
}

/* ── mappers ─────────────────────────────────────────────────────────────── */

function mapMatch(dto: JobMatchDto): JobResult {
  return {
    id: `${dto.source.toLowerCase()}-${dto.sourceJobId}`,
    source: dto.source, sourceJobId: dto.sourceJobId,
    title: dto.title, company: dto.company, location: dto.location,
    distanceMiles: dto.distanceMiles, employmentType: dto.employmentType,
    workMode: dto.workMode,
    salaryMinGbp: dto.salaryMin ?? undefined, salaryMaxGbp: dto.salaryMax ?? undefined,
    dayRateMinGbp: dto.dayRateMin ?? undefined, dayRateMaxGbp: dto.dayRateMax ?? undefined,
    contractMonths: dto.contractMonths ?? undefined,
    postedDate: typeof dto.postedDate === "string" ? dto.postedDate : String(dto.postedDate),
    score: dto.score, recommended: dto.recommended,
    reasons: dto.reasons ?? [], risks: dto.risks ?? [], url: dto.url ?? "",
  };
}

function mapDetail(dto: JobDto): JobDetail {
  return {
    id: `${dto.source.toLowerCase()}-${dto.sourceJobId}`,
    source: dto.source, sourceJobId: dto.sourceJobId,
    title: dto.title, company: dto.company, location: dto.location,
    distanceMiles: dto.distanceMiles, employmentType: dto.employmentType,
    workMode: dto.workMode,
    salaryMinGbp: dto.salaryMin ?? undefined, salaryMaxGbp: dto.salaryMax ?? undefined,
    dayRateMinGbp: dto.dayRateMin ?? undefined, dayRateMaxGbp: dto.dayRateMax ?? undefined,
    contractMonths: dto.contractMonths ?? undefined,
    postedDate: typeof dto.postedDate === "string" ? dto.postedDate : String(dto.postedDate),
    score: 0, recommended: false, reasons: [], risks: [],
    url: dto.url ?? "", description: dto.description ?? "",
  };
}

/* ── public API ──────────────────────────────────────────────────────────── */

export type FetchResult =
  | { status: "ok"; jobs: JobResult[]; sourceStatus: SourceStatus[]; rejectedSummary: Record<string, number> }
  | { status: "error"; message: string };

export async function fetchMatches(): Promise<FetchResult> {
  try {
    const res = await fetch(`${BASE_URL}/api/jobs/results`);
    if (!res.ok) return { status: "error", message: `API ${res.status} ${res.statusText}` };
    const data: JobMatchDto[] | SearchResponse = await res.json();
    // Handle both old array response and new SearchResponse shape
    if (Array.isArray(data)) {
      return { status: "ok", jobs: data.map(mapMatch), sourceStatus: [], rejectedSummary: {} };
    }
    return {
      status: "ok",
      jobs: data.matches.map(mapMatch),
      sourceStatus: data.sourceStatus ?? [],
      rejectedSummary: data.rejectedSummary ?? {},
    };
  } catch {
    return { status: "error", message: "Could not reach the API. Is the server running with --http?" };
  }
}

export type DetailResult = { status: "ok"; job: JobDetail } | { status: "error"; message: string };

export async function fetchJobDetail(source: string, sourceJobId: string): Promise<DetailResult> {
  try {
    const res = await fetch(`${BASE_URL}/api/jobs/${encodeURIComponent(source)}/${encodeURIComponent(sourceJobId)}`);
    if (!res.ok) return { status: "error", message: `API ${res.status}` };
    const body: { found: boolean; job?: JobDto; message?: string } = await res.json();
    if (!body.found || !body.job) return { status: "error", message: body.message ?? "Not found" };
    return { status: "ok", job: mapDetail(body.job) };
  } catch {
    return { status: "error", message: "Could not reach the API." };
  }
}

export async function getConfig(): Promise<Record<string, string>> {
  const res = await fetch(`${BASE_URL}/api/config`);
  if (!res.ok) throw new Error("Failed to fetch config");
  return res.json();
}

export async function saveConfig(config: Record<string, string>): Promise<void> {
  const res = await fetch(`${BASE_URL}/api/config`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(config),
  });
  if (!res.ok) throw new Error("Failed to save config");
}

export type CvUploadMode = "replace" | "rename";

export async function fetchCvs(): Promise<CvFile[]> {
  const res = await fetch(`${BASE_URL}/api/cvs`);
  if (!res.ok) throw new Error("Failed to fetch CV files");
  return res.json();
}

export async function uploadCv(file: File, mode: CvUploadMode, targetName: string): Promise<CvFile> {
  const form = new FormData();
  form.append("file", file);
  form.append("mode", mode);
  form.append("targetName", targetName);

  const res = await fetch(`${BASE_URL}/api/cvs/upload`, {
    method: "POST",
    body: form,
  });

  if (!res.ok) {
    const body: { message?: string } = await res.json().catch(() => ({}));
    throw new Error(body.message ?? "Failed to upload CV");
  }

  return res.json();
}
