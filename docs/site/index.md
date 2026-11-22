---
layout: home
title: Levy
---

<p align="center"><img src="assets/levy-breaker.svg" alt="Levy" width="320"></p>

Levy is a Windows desktop app for property tax assessors, written in C# / .NET 8 with WPF and backed by Supabase (Postgres, Auth and row-level security). It's a portfolio piece for roles building assessor and government software. It's set in Shreveport, Louisiana, and every name, value and sale in it is made up.

It has two sides:

- **Valuation:** mass appraisal where the valuation rules are code. Edit them, press Run, and see the effect on a parcel map, an IAAO ratio study and tax bills.
- **Workflow:** jobs, projects, a scrum board, tasks, clients and reports for the office, plus bug reports and feature requests that go to the developers in GitLab and come back.

## Pages

| Page | What it covers |
|---|---|
| [Getting started](getting-started.md) | Building, running and signing in. |
| [Valuation](valuation.md) | The rules language, the valuer and the ratio study (median, COD, PRD). |
| [Workflow pages](workflow.md) | Jobs, projects, the scrum board, tasks, clients, reports, requests and GitLab intake, team, activity and companies. |
| [Accounts and security](accounts.md) | Supabase sign-in, row-level security, company codes, profiles, badges, company tags and colours. |
| [Architecture](architecture.md) | The engine, the desktop app's layout rules, themes and the database migrations. |
| [CI/CD](ci-cd.md) | Build and tests, database tests, and deploying migrations. |

The source is at [github.com/Karkaton-V/Levy](https://github.com/Karkaton-V/Levy). The repository's `docs/HANDOFF.md` is the build log (what was built, when, and how it was tested), and `docs/GITLAB.md` has the GitLab setup.
