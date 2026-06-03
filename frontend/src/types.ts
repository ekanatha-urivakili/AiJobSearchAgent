export type EmploymentType = "Permanent" | "Contract";
export type WorkMode = "Remote" | "Hybrid" | "Office";

export interface JobResult {
  id: string;
  source: string;
  sourceJobId: string;
  title: string;
  company: string;
  location: string;
  distanceMiles: number;
  employmentType: EmploymentType;
  workMode: WorkMode;
  salaryMinGbp?: number;
  salaryMaxGbp?: number;
  dayRateMinGbp?: number;
  dayRateMaxGbp?: number;
  contractMonths?: number;
  postedDate: string;
  score: number;
  recommended: boolean;
  reasons: string[];
  risks: string[];
  url: string;
}

export interface JobDetail extends JobResult {
  description: string;
}

export interface SourceStatus {
  source: string;
  status: string;
  jobsFetched: number;
  mode: string;
  warnings: string[];
}
