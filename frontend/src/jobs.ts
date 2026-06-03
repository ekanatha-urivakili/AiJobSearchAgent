import type { JobResult } from "./types";

export const jobResults: JobResult[] = [
  {
    id: "reed-001", sourceJobId: "001", source: "Reed",
    title: "Senior Fullstack Engineer", company: "FinTech Platform",
    location: "Milton Keynes", distanceMiles: 6.4,
    employmentType: "Permanent", workMode: "Hybrid",
    salaryMinGbp: 80000, salaryMaxGbp: 95000,
    postedDate: "2026-05-30", score: 92, recommended: true,
    reasons: ["Strong .NET and React alignment", "Salary above target", "Hybrid within preferred radius"],
    risks: ["Financial services domain may require regulatory experience"],
    url: "https://www.reed.co.uk/jobs/senior-fullstack-engineer-jobs",
  },
  {
    id: "indeed-002", sourceJobId: "002", source: "Indeed UK",
    title: "Lead Software Engineer", company: "E-Commerce Scale-up",
    location: "London", distanceMiles: 47,
    employmentType: "Contract", workMode: "Remote",
    dayRateMinGbp: 500, dayRateMaxGbp: 600, contractMonths: 6,
    postedDate: "2026-05-28", score: 87, recommended: true,
    reasons: ["C# and AWS skills match", "Day rate above floor", "Remote work mode"],
    risks: ["47 miles to client site if required on-site"],
    url: "https://uk.indeed.com/viewjob?jk=example002",
  },
  {
    id: "reed-003", sourceJobId: "003", source: "Reed",
    title: "Principal Developer", company: "Enterprise SaaS Ltd",
    location: "Birmingham", distanceMiles: 38,
    employmentType: "Permanent", workMode: "Hybrid",
    salaryMinGbp: 90000, salaryMaxGbp: 110000,
    postedDate: "2026-06-01", score: 78, recommended: false,
    reasons: ["Seniority match", "Salary range excellent"],
    risks: ["Birmingham commute 38 miles", "No TypeScript mentioned"],
    url: "https://www.reed.co.uk/jobs/principal-developer-jobs",
  },
];
