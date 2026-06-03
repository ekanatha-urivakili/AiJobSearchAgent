import type { EmploymentType, JobResult, WorkMode } from "./types";

  const BASE_URL = (import.meta.env.VITE_API_BASE_URL as string | undefined) ?? "http://localhost:5001";

interface JobMatchDto {
  source: string;
  sourceJobId: string;
  title: string;
  company: string;
  location: string;
  distanceMiles: number;
  employmentType: EmploymentType;
  workMode: WorkMode;
  salaryMin: number | null;
  salaryMax: number | null;
  dayRateMin: number | null;
  dayRateMax: number | null;
  contractMonths: number | null;
  postedDate: string;
  score: number;
  recommended: boolean;
  reasons: string[];
  risks: string[];
  url: string;
}

export type FetchResult =
  | { status: "ok"; jobs: JobResult[] }
  | { status: "error"; message: string };

function mapMatch(dto: JobMatchDto): JobResult {
  return {
    id: `${dto.source.toLowerCase()}-${dto.sourceJobId}`,
    source: dto.source,
    title: dto.title,
    company: dto.company,
    location: dto.location,
    distanceMiles: dto.distanceMiles,
    employmentType: dto.employmentType,
    workMode: dto.workMode,
    salaryMinGbp: dto.salaryMin ?? undefined,
    salaryMaxGbp: dto.salaryMax ?? undefined,
    dayRateMinGbp: dto.dayRateMin ?? undefined,
    dayRateMaxGbp: dto.dayRateMax ?? undefined,
    contractMonths: dto.contractMonths ?? undefined,
    postedDate: dto.postedDate,
    score: dto.score,
    recommended: dto.recommended,
    reasons: dto.reasons ?? [],
    risks: dto.risks ?? [],
    url: dto.url ?? "",
  };
}

export async function fetchMatches(): Promise<FetchResult> {
  try {
    const res = await fetch(`${BASE_URL}/api/jobs/search`);
    if (!res.ok) {
      return { status: "error", message: `API responded with ${res.status} ${res.statusText}` };
    }
    const matches: JobMatchDto[] = await res.json();
    return { status: "ok", jobs: matches.map(mapMatch) };
  } catch {
    return { status: "error", message: "Could not reach the job search API. Is the server running with --http?" };
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
