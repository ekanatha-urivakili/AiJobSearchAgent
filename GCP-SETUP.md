

⚠️ Prerequisite: 
You need Google Workspace, not personal Gmail
Domain-Wide Delegation only works with Google Workspace (paid org account). It will never work with a @gmail.com personal account — Google blocks it by design.
Your options:

Option A — You already have a Google Workspace org → follow the steps below
Option B — You don't → use OAuth 2.0 user credentials instead (different code path, works with personal Gmail — I can help with that separately)

If you have Workspace, proceed:

Step 1: GCP Project & Gmail API

Go to console.cloud.google.com
Select or create a project (e.g. ai-job-search-agent)
Navigate to APIs & Services → Library
Search for Gmail API → click Enable


Step 2: Create a Service Account

Go to IAM & Admin → Service Accounts
Click Create Service Account

Name: job-search-gmail
ID: auto-fills
Click Create and Continue


Skip the role grants (no GCP roles needed — Gmail access is via DWD, not IAM)
Click Done


Step 3: Create & Download JSON Key

Click into the service account you just created
Go to the Keys tab
Click Add Key → Create new key
Select JSON → click Create
A .json file downloads — this is your GMAIL_CREDENTIALS_JSON


Step 4: Enable Domain-Wide Delegation on the Service Account

Still on the service account page, go to the Details tab
Click Edit (pencil icon)
Expand Advanced settings (or look for "Domain-wide delegation")
Check Enable Google Workspace Domain-wide Delegation
Give it a name (e.g. Job Search Gmail Access)
Click Save
Copy the Client ID shown — it's a long number like 123456789012345678901. You'll need this in the next step.


Step 5: Authorize in Google Workspace Admin Console
This is the step that actually grants the permission — GCP alone isn't enough.

Go to admin.google.com (must be a Workspace super admin)
Navigate to Security → Access and data control → API controls
Click Manage Domain Wide Delegation at the bottom
Click Add new
Fill in:

Client ID: paste the number from Step 4
OAuth Scopes: https://www.googleapis.com/auth/gmail.readonly


Click Authorize


Step 6: Configure Your App
Set these environment variables (in .env or Railway/Docker config):
GMAIL_CREDENTIALS_JSON=<contents of the JSON key file downloaded in Step 3>
GMAIL_USER_EMAIL=yourname@yourdomain.com
GMAIL_USER_EMAIL must be an actual mailbox in your Workspace org — the service account impersonates that user. It's the inbox that receives the job alert emails.

Step 7: Verify
In the app, call sources.health — Gmail Alerts and Indeed UK should now show Ready: true. Then run jobs.search and the unauthorized_client error should be gone.

If you're on personal Gmail
The simpler alternative is OAuth 2.0 with a refresh token — you authenticate once in a browser, store the refresh token, and the app uses it going forward. No Workspace org needed. Let me know and I'll walk through that code path instead.