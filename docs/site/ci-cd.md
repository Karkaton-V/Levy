---
layout: page
title: CI/CD
permalink: /ci-cd/
---

Two GitHub Actions workflows run the project: `.github/workflows/ci.yml` for the app and database, and `.github/workflows/pages.yml` for this site.

## ci.yml
Runs on every push and on pull requests to `main`. A newer push to the same branch cancels the run in progress.

| Job | Runs on | What it does |
|---|---|---|
| **Build and test** | Windows | Restores and builds `ValuationWorkbench.sln` in Release, runs the engine tests, and keeps the test results as an artifact. Windows because the app is WPF. |
| **Migrations and security tests** | Ubuntu | `supabase db start` applies every migration to a fresh, throwaway Postgres, so a broken migration fails before merge. Then `supabase test db` runs the pgTAP tests in `supabase/tests/database`. Needs no secrets. |
| **Apply migrations to Supabase** | Ubuntu | Only on a push to `main`, after both jobs pass. Links the hosted project and runs `supabase db push --remote`, which applies only migrations the project hasn't recorded yet. |

The Supabase CLI version is pinned in the workflow so runs don't change under it.

### The database tests
The pgTAP files cover:
- `security.test.sql`: sign-up seeding, joining by code, company isolation, pending lock-out, approval, the last-admin guard, platform-admin-only functions, the self-promotion block, no deletes, request triage rights, activity log limits and signed-out access.
- `profiles.test.sql`: profile edits, the picture bucket, badges and account deletion.
- `company_tags.test.sql`: tag icons and letters, who can set them, and the pronoun choices.
- `code_theme.test.sql`: the code colour choices and who can set them.

### The deploy job's secrets
The deploy runs in the `production` environment and needs two repository secrets (Settings → Secrets and variables → Actions):
- `SUPABASE_ACCESS_TOKEN`: a Supabase personal access token.
- `SUPABASE_DB_PASSWORD`: the project's database password.

Without them the job stops with a message naming the missing secret. Required reviewers on the `production` environment make a person approve each deploy.

## pages.yml
Builds this site from `docs/site` with Jekyll on every pull request that changes it, and publishes it to GitHub Pages on a push to `main`. The repository's Pages source must be set to **GitHub Actions** (Settings → Pages).
