#!/usr/bin/env bash
# Ingest Indeed jobs then trigger a fresh search.
# Run from the project root: bash ingest_indeed.sh

set -euo pipefail

BASE="http://localhost:5001"

echo "→ Ingesting Indeed jobs…"
curl -s -X POST "$BASE/api/jobs/ingest_indeed" \
  -H "Content-Type: application/json" \
  -d '{
    "clearFirst": true,
    "jobs": [
      {
        "jobId": "JOBSEARCH_1",
        "title": "Senior Software Engineer (Back-End)",
        "company": "CGI",
        "location": "United Kingdom",
        "url": "https://to.indeed.com/aavg4llqszwq",
        "employmentType": 0,
        "workMode": 2,
        "salaryMin": null, "salaryMax": null,
        "dayRateMin": null, "dayRateMax": null,
        "contractMonths": null,
        "description": "Senior Back-End Software Engineer role at CGI."
      },
      {
        "jobId": "JOBSEARCH_2",
        "title": "Senior Software Engineer",
        "company": "UNiDAYS",
        "location": "London",
        "url": "https://to.indeed.com/aa46xylmz9ph",
        "employmentType": 0,
        "workMode": 1,
        "salaryMin": null, "salaryMax": null,
        "dayRateMin": null, "dayRateMax": null,
        "contractMonths": null,
        "description": "Senior Software Engineer at UNiDAYS, London."
      },
      {
        "jobId": "JOBSEARCH_3",
        "title": "Backend Software Engineer C# .Net SQL - Sports Trading",
        "company": "Client Server",
        "location": "London",
        "url": "https://to.indeed.com/aagnsjdljqp2",
        "employmentType": 0,
        "workMode": 2,
        "salaryMin": null, "salaryMax": null,
        "dayRateMin": null, "dayRateMax": null,
        "contractMonths": null,
        "description": "C# .NET SQL backend engineer for sports trading platform."
      },
      {
        "jobId": "JOBSEARCH_5",
        "title": "Senior Software Engineer .Net Python SQL - FTC",
        "company": "Client Server",
        "location": "London",
        "url": "https://to.indeed.com/aabr8crtd4hr",
        "employmentType": 0,
        "workMode": 1,
        "salaryMin": null, "salaryMax": null,
        "dayRateMin": null, "dayRateMax": null,
        "contractMonths": null,
        "description": "Senior .NET Python SQL engineer, fixed-term contract."
      },
      {
        "jobId": "JOBSEARCH_6",
        "title": "Senior C# Developer - Sports Trading",
        "company": "Client Server",
        "location": "London",
        "url": "https://to.indeed.com/aadn9mf6sxz8",
        "employmentType": 0,
        "workMode": 2,
        "salaryMin": null, "salaryMax": null,
        "dayRateMin": null, "dayRateMax": null,
        "contractMonths": null,
        "description": "Senior C# .NET developer for sports trading systems."
      },
      {
        "jobId": "JOBSEARCH_7",
        "title": ".NET Developer",
        "company": "Noir",
        "location": "London",
        "url": "https://to.indeed.com/aasvnwrlcstb",
        "employmentType": 0,
        "workMode": 1,
        "salaryMin": null, "salaryMax": null,
        "dayRateMin": null, "dayRateMax": null,
        "contractMonths": null,
        "description": ".NET Developer position at Noir, London."
      },
      {
        "jobId": "JOBSEARCH_10",
        "title": "C#/OO Software Engineer",
        "company": "Redhorse International",
        "location": "London",
        "url": "https://to.indeed.com/aakpswr7nfnd",
        "employmentType": 0,
        "workMode": 1,
        "salaryMin": null, "salaryMax": null,
        "dayRateMin": null, "dayRateMax": null,
        "contractMonths": null,
        "description": "C# object-oriented software engineer at Redhorse International."
      },
      {
        "jobId": "JOBSEARCH_15",
        "title": "Software Developer (Artificial Intelligence)",
        "company": "Oxford Economics",
        "location": "Remote",
        "url": "https://to.indeed.com/aask6m49b2w7",
        "employmentType": 0,
        "workMode": 0,
        "salaryMin": null, "salaryMax": null,
        "dayRateMin": null, "dayRateMax": null,
        "contractMonths": null,
        "description": "AI-focused software developer at Oxford Economics, fully remote."
      }
    ]
  }' | python3 -m json.tool

echo ""
echo "→ Triggering job search…"
curl -s "$BASE/api/jobs/search" | python3 -c "
import json, sys
data = json.load(sys.stdin)
statuses = data.get('sourceStatus', [])
matches  = data.get('matches', [])

print('\n=== Source Status ===')
for s in statuses:
    print(f\"  {s['source']:15} {s['status']:10} jobs={s['jobsFetched']}  warnings={len(s.get('warnings', []))}\")

print(f'\n=== Matches ({len(matches)} total) ===')
for m in matches[:10]:
    sal = ''
    if m.get('salaryMin'): sal = f\"  £{int(m['salaryMin']):,}–£{int(m['salaryMax'] or m['salaryMin']):,}\"
    print(f\"  [{m['score']:>2}] {m['source']:15} {m['title'][:45]:45}  {m['company'][:25]}{sal}\")
"

echo ""
echo "✓ Done — refresh http://localhost:5173 to see updated results."
