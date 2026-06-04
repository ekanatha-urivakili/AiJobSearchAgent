#!/usr/bin/env bash
# Ingest jobs from Indeed Direct, Dice, and ZipRecruiter, then trigger a fresh search.
# Run from the project root: bash ingest_jobs.sh

set -euo pipefail
BASE="http://localhost:5001"

# ── Indeed Direct ────────────────────────────────────────────────────────────
echo "→ Ingesting Indeed Direct jobs…"
curl -s -X POST "$BASE/api/jobs/ingest_indeed" \
  -H "Content-Type: application/json" \
  -d '{
    "clearFirst": true,
    "jobs": [
      { "jobId":"JOBSEARCH_1","title":"Senior Software Engineer (Back-End)","company":"CGI","location":"United Kingdom","url":"https://to.indeed.com/aavg4llqszwq","employmentType":0,"workMode":2,"salaryMin":null,"salaryMax":null,"dayRateMin":null,"dayRateMax":null,"contractMonths":null,"description":"Senior Back-End Software Engineer at CGI." },
      { "jobId":"JOBSEARCH_2","title":"Senior Software Engineer","company":"UNiDAYS","location":"London","url":"https://to.indeed.com/aa46xylmz9ph","employmentType":0,"workMode":1,"salaryMin":null,"salaryMax":null,"dayRateMin":null,"dayRateMax":null,"contractMonths":null,"description":"Senior Software Engineer at UNiDAYS, London." },
      { "jobId":"JOBSEARCH_3","title":"Backend Software Engineer C# .Net SQL - Sports Trading","company":"Client Server","location":"London","url":"https://to.indeed.com/aagnsjdljqp2","employmentType":0,"workMode":2,"salaryMin":null,"salaryMax":null,"dayRateMin":null,"dayRateMax":null,"contractMonths":null,"description":"C# .NET SQL backend engineer for sports trading platform." },
      { "jobId":"JOBSEARCH_5","title":"Senior Software Engineer .Net Python SQL","company":"Client Server","location":"London","url":"https://to.indeed.com/aabr8crtd4hr","employmentType":0,"workMode":1,"salaryMin":null,"salaryMax":null,"dayRateMin":null,"dayRateMax":null,"contractMonths":null,"description":"Senior .NET Python SQL engineer." },
      { "jobId":"JOBSEARCH_6","title":"Senior C# Developer - Sports Trading","company":"Client Server","location":"London","url":"https://to.indeed.com/aadn9mf6sxz8","employmentType":0,"workMode":2,"salaryMin":null,"salaryMax":null,"dayRateMin":null,"dayRateMax":null,"contractMonths":null,"description":"Senior C# .NET developer for sports trading systems." },
      { "jobId":"JOBSEARCH_7","title":".NET Developer","company":"Noir","location":"London","url":"https://to.indeed.com/aasvnwrlcstb","employmentType":0,"workMode":1,"salaryMin":null,"salaryMax":null,"dayRateMin":null,"dayRateMax":null,"contractMonths":null,"description":".NET Developer at Noir, London." },
      { "jobId":"JOBSEARCH_10","title":"C#/OO Software Engineer","company":"Redhorse International","location":"London","url":"https://to.indeed.com/aakpswr7nfnd","employmentType":0,"workMode":1,"salaryMin":null,"salaryMax":null,"dayRateMin":null,"dayRateMax":null,"contractMonths":null,"description":"C# OO software engineer at Redhorse International." },
      { "jobId":"JOBSEARCH_15","title":"Software Developer (Artificial Intelligence)","company":"Oxford Economics","location":"Remote","url":"https://to.indeed.com/aask6m49b2w7","employmentType":0,"workMode":0,"salaryMin":null,"salaryMax":null,"dayRateMin":null,"dayRateMax":null,"contractMonths":null,"description":"AI-focused software developer at Oxford Economics, fully remote." }
    ]}' | python3 -m json.tool

# ── Dice ─────────────────────────────────────────────────────────────────────
echo ""
echo "→ Ingesting Dice jobs…"
curl -s -X POST "$BASE/api/jobs/ingest_dice" \
  -H "Content-Type: application/json" \
  -d '{
    "clearFirst": true,
    "jobs": [
      { "jobId":"60da0443-3c8e-498f-afad-ed11e9243926","title":"Sr. AI Software Engineer (C#/.Net) - 100% REMOTE","company":"Jobot","location":"Remote","url":"https://www.dice.com/job-detail/60da0443-3c8e-498f-afad-ed11e9243926","employmentType":0,"workMode":0,"salaryRaw":"USD 170,000.00 - 270,000.00 per year","description":"100% REMOTE. Senior AI-focused C#/.NET engineer for fast-growing infrastructure company." },
      { "jobId":"bf5a9ba07ddcf229651068c9c6bd770f","title":"Senior C# .NET Engineer","company":"Diverse Lynx","location":"Remote","url":"https://www.dice.com/job-detail/bf5a9ba07ddcf229651068c9c6bd770f","employmentType":0,"workMode":0,"salaryRaw":"USD 109,100.00 - 149,800.00 per year","description":"Senior C#/.NET engineer, fully remote." },
      { "jobId":"0118746a1dc23ca01bcb4455062a7ce8","title":"Senior Software Engineer .NET Azure","company":"Very LLC","location":"Remote","url":"https://www.dice.com/job-detail/0118746a1dc23ca01bcb4455062a7ce8","employmentType":1,"workMode":0,"salaryRaw":"USD 127,100.00 - 167,600.00 per year","description":"Senior .NET/Azure engineer, contract (1099), remote." },
      { "jobId":"e557518a-80a3-4ef3-9763-c33791ab1507","title":"Backend Engineer C# Microservices","company":"TechForce","location":"Remote","url":"https://www.dice.com/job-detail/e557518a-80a3-4ef3-9763-c33791ab1507","employmentType":0,"workMode":0,"salaryRaw":"USD 130,000.00 - 160,000.00 per year","description":"Backend microservices engineer, C# .NET, remote-first team." },
      { "jobId":"1706a484-395e-4d2a-88cb-288f5ce9b5e4","title":"Principal Software Engineer C# Cloud","company":"CloudSys Inc","location":"Remote","url":"https://www.dice.com/job-detail/1706a484-395e-4d2a-88cb-288f5ce9b5e4","employmentType":0,"workMode":0,"salaryRaw":"USD 180,000.00 - 220,000.00 per year","description":"Principal-level C# cloud engineer. Remote. Greenfield platform build." }
    ]}' | python3 -m json.tool

# ── ZipRecruiter ──────────────────────────────────────────────────────────────
echo ""
echo "→ Ingesting ZipRecruiter jobs…"
curl -s -X POST "$BASE/api/jobs/ingest_ziprecruiter" \
  -H "Content-Type: application/json" \
  -d '{
    "clearFirst": true,
    "jobs": [
      { "jobId":"cfe0d60aa25f488e","title":"Staff Software Engineer - Full Stack C# .NET SignalR","company":"2Bridge Partners","location":"New York, NY","url":"https://www.ziprecruiter.com/c/2Bridge-Partners/Job/Staff-Software-Engineer-Full-Stack-C-.net-Websockets-SignalR/-in-New-York,NY?jid=cfe0d60aa25f488e","employmentType":0,"workMode":0,"salaryMinUsd":200000,"salaryMaxUsd":350000,"description":"Staff-level full-stack C#/.NET with WebSockets and SignalR. Remote optional." },
      { "jobId":"22b6f2da3f458446","title":"Senior C# .NET Engineer","company":"Diverse Lynx","location":"Jersey City, NJ","url":"https://www.ziprecruiter.com/c/Diverse-Lynx/Job/Senior-C--.NET-Engineer/-in-Jersey-City,NJ?jid=22b6f2da3f458446","employmentType":0,"workMode":0,"salaryMinUsd":109100,"salaryMaxUsd":149800,"description":"Senior C#/.NET Engineer, remote." },
      { "jobId":"d599f3b34ba22299","title":"Senior Software Engineer .NET Azure - Contract","company":"Very LLC","location":"New Rochelle, NY","url":"https://www.ziprecruiter.com/c/Very-LLC/Job/Senior-Software-Engineer-(.NET-Azure)-Contract-(1099)/-in-New-Rochelle,NY?jid=d599f3b34ba22299","employmentType":1,"workMode":0,"salaryMinUsd":127100,"salaryMaxUsd":167600,"description":"Senior .NET/Azure contract engineer (1099), remote optional." },
      { "jobId":"fbb1cdd7b07021ea","title":"Senior Software Engineer","company":"LPL Financial","location":"New York, NY","url":"https://www.ziprecruiter.com/c/LPL-Financial/Job/Senior-Software-Engineer/-in-New-York,NY?jid=fbb1cdd7b07021ea","employmentType":0,"workMode":1,"salaryMinUsd":116800,"salaryMaxUsd":194600,"description":"Senior Software Engineer at LPL Financial. Hybrid." },
      { "jobId":"555a2a2b1264a293","title":"Senior Software Engineer - Node TypeScript Frontend","company":"Sourcemap","location":"New York, NY","url":"https://www.ziprecruiter.com/c/Sourcemap/Job/Senior-Software-Engineer-Node,-TypeScript,-Frontend-Ecosystem/-in-New-York,NY?jid=555a2a2b1264a293","employmentType":0,"workMode":1,"salaryMinUsd":null,"salaryMaxUsd":null,"description":"Senior engineer on Node/TypeScript/Frontend stack at Sourcemap." }
    ]}' | python3 -m json.tool

# ── Trigger search ────────────────────────────────────────────────────────────
echo ""
echo "→ Triggering job search across all sources…"
curl -s "$BASE/api/jobs/search" | python3 -c "
import json, sys
d = json.load(sys.stdin)
print('\n=== Source Status ===')
for s in d.get('sourceStatus', []):
    warn = s.get('warnings', [])
    w = f'  ⚠ {warn[0][:60]}' if warn else ''
    print(f\"  {s['source']:18} {s['status']:10} jobs={s['jobsFetched']}{w}\")
print(f\"\n=== Matches ({len(d.get('matches', []))} total) ===\")
for m in d.get('matches', [])[:15]:
    sal = ''
    if m.get('salaryMin'): sal = f\"  \${int(m['salaryMin']):,}–\${int(m.get('salaryMax') or m['salaryMin']):,}\"
    print(f\"  [{m['score']:>2}] {m['source']:18} {m['title'][:42]:42}  {m['company'][:22]}{sal}\")
"

echo ""
echo "✓ Done — refresh http://localhost:5173 to see all sources."
