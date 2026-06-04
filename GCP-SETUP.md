# GCP Gmail Setup

## Prerequisite

This implementation uses a Google service account with Google Workspace domain-wide delegation. It does not work with a personal `@gmail.com` mailbox.

Options:

- Google Workspace org: follow the steps below.
- Personal Gmail: use an OAuth 2.0 user credential flow instead. That is a different code path and is not implemented in the current Gmail adapters.

## Step 1: GCP Project And Gmail API

1. Go to Google Cloud Console.
2. Select or create a project, for example `ai-job-search-agent`.
3. Navigate to APIs & Services -> Library.
4. Search for Gmail API and enable it.

## Step 2: Create A Service Account

1. Go to IAM & Admin -> Service Accounts.
2. Click Create Service Account.
3. Use a name such as `job-search-gmail`.
4. Click Create and Continue.
5. Skip role grants. Gmail mailbox access comes from domain-wide delegation, not IAM roles.
6. Click Done.

## Step 3: Create And Download JSON Key

1. Open the service account.
2. Go to the Keys tab.
3. Click Add Key -> Create new key.
4. Select JSON and create the key.
5. Save the downloaded JSON. Its single-line contents become `GMAIL_CREDENTIALS_JSON`.

## Step 4: Enable Domain-Wide Delegation

1. Open the service account Details tab.
2. Click Edit.
3. Expand Advanced settings or find Domain-wide delegation.
4. Enable Google Workspace Domain-wide Delegation.
5. Set a product name such as `Job Search Gmail Access`.
6. Save.
7. Copy the numeric Client ID shown for the service account.

## Step 5: Authorize In Google Workspace Admin

This grants the mailbox permission. GCP setup alone is not enough.

1. Go to Google Admin Console as a Workspace super admin.
2. Navigate to Security -> Access and data control -> API controls.
3. Open Manage Domain Wide Delegation.
4. Click Add new.
5. Use the Client ID from Step 4.
6. Add this OAuth scope:

```text
https://www.googleapis.com/auth/gmail.readonly
```

7. Click Authorize.

## Step 6: Configure The App

Set these values in `.env`, Railway variables, Docker config, or the Settings screen:

```text
GMAIL_CREDENTIALS_JSON=<contents of the JSON key file downloaded in Step 3>
GMAIL_USER_EMAIL=yourname@yourdomain.com
GMAIL_SEARCH_QUERY=label:job-alerts is:unread
INDEED_GMAIL_SEARCH_QUERY=from:jobalerts-noreply@indeed.com is:unread
```

`GMAIL_USER_EMAIL` must be an actual mailbox in the Workspace org. The service account impersonates this mailbox, and this mailbox must receive the job alert emails.

## Step 7: Verify

1. Start the MCP server in HTTP mode or STDIO mode.
2. Call `sources.health`.
3. `Gmail Alerts` and `Indeed UK` should show `ready=true` when both `GMAIL_CREDENTIALS_JSON` and `GMAIL_USER_EMAIL` are configured.
4. Run `jobs.search` or call `GET /api/jobs/search`.

If the setup is correct, Gmail-backed sources should no longer return `unauthorized_client`.
