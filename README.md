<p align="center"><img src="docs/brand/levy-breaker.svg" alt="Levy" width="320"></p>

# Levy

[![CI](https://github.com/Karkaton-V/Levy/actions/workflows/ci.yml/badge.svg)](https://github.com/Karkaton-V/Levy/actions/workflows/ci.yml)

A Windows desktop app for property tax assessors. It has two sides:

- **Valuation:** mass appraisal where the valuation rules are code. Edit them, press Run, and see the effect on a parcel map, an IAAO ratio study and tax bills.
- **Workflow:** jobs, projects, a scrum board, tasks, clients and reports for the office, plus bug reports and feature requests that flow to the developers in GitLab and back.

Built with C# / .NET 8 and WPF, with Supabase (Postgres, Auth, row-level security) behind it. It's a portfolio piece for government-software roles. It's set in Shreveport, Louisiana, and every name, value and sale in it is made up.

## Run it

1. Open `ValuationWorkbench.sln` in JetBrains Rider (or Visual Studio) on Windows with the .NET 8 SDK or newer.
2. Run the `Workbench.Desktop` configuration.
3. Choose **Create account**, and leave **Company code** blank. You get an office of your own with sample clients, jobs, projects, tasks and reports. You're its admin.
4. On **Rules**, change `LandRate.N-04 = 9.00` to `11.50` and press F5, then look at **Parcel map** and **Compare runs**.

**Internet use:**
- The app signs in to the Supabase project in `Assets/Account/supabase.json`. That file holds a publishable key, which is safe to ship because row-level security protects the data.
- The map needs an internet connection for its tiles.
- The first launch downloads the study area's streets and buildings from OpenStreetMap, then keeps them in `%LOCALAPPDATA%\ValuationWorkbench\buildings`.

## What's in it

### Valuation
- **Rules:** a code editor (AvalonEdit) for the valuation rules, with syntax colours, live problem squiggles, hover help, completion and F5 to run.
- **Parcel map:**
  - **Overview:** a simplified Louisiana map. Click a city, then the study area. The full-size map (Mapsui and OpenStreetMap) has search and zoom.
  - **Parcel record:** clicking a parcel shows its record and a step-by-step "why this value" trace. Each step links back to the rule lines behind it.
- **Compare runs:** before and after statistics for median ratio, COD and PRD against IAAO targets, with each neighborhood's result.

### Workflow (one company's shared data)
- **Jobs, Projects, Tasks, Clients, Reports:**
  - counters, search, a status filter and "Assigned to me";
  - sortable tables with status chips and overdue dates;
  - an edit form beside the table.
- **Scrum board:** five status columns; dragging a card saves its new status.
- **Requests:** anyone in the company reports a bug or asks for a feature, and Levy records where they were (page and parcel). They then follow it to **Fixed** without needing GitLab.

### Accounts, companies and admins
- **Signing up:**
  - **Company code:** people sign up with their company's code and wait for an admin to approve them. Until then, they see nothing.
  - **No code:** without one, they start an office of their own.
- **Team** (company admins): approve or decline people, set job titles (shown under the logo), roles and access, and add existing accounts by email.
- **Activity** (company admins): who did what, to which record, on what date and at what time. The database writes it with triggers, so the app can't skip or forge an entry, and nobody can edit it.
- **Companies** (platform admins): create and rename companies, assign people, and choose each company's account manager.
- **Intake** (account managers):
  - triage requests from the companies they manage, setting priority, codebase (Rails or .NET) and a note;
  - **Send to GitLab** opens the issue with `type::`, `priority::`, `stack::` and `client::` labels;
  - Levy then reads back the issue, its merge requests and their pipelines, and shows the status to the client.
- **Settings menu:** in the side panel, with light and dark themes that switch instantly, and sign out.

### GitLab, live or demo
`Assets/Account/gitlab.json` starts in **demo mode**.

**Demo mode:**
- A **mock GitLab** is kept on your PC, so the whole request → issue → merge request → review → merge flow works with no server.
- In the Mock GitLab window you play developer and reviewer: assign, start work, open a draft merge request, run the pipeline (pass or fail), mark it ready, approve and merge.
- Its rules: the pipeline must pass, drafts can't merge, and authors can't approve their own merge request.

**Live:** point the file at your own GitLab (for example a self-managed one) to send real issues and **Sign in with GitLab**. See [docs/GITLAB.md](docs/GITLAB.md) for the workflow and setup.

## How it's built

| Part | What it is |
|---|---|
| `src/Workbench.Engine` | The rules parser, cost-approach valuation with a step-by-step trace, the ratio study (median, COD, PRD) and sample parcels. Plain .NET 8, no UI. |
| `src/Workbench.Desktop` | The WPF app. |
| `tests/Workbench.Engine.Tests` | xUnit tests for the engine. |
| `supabase/migrations` | The database: tables, row-level security, the functions the app calls, triggers for sign-up, the activity log and request stamping. |
| `supabase/tests` | 24 pgTAP security tests, run in CI against a fresh database. |
| `.github/workflows/ci.yml` | CI/CD. |

### Desktop app layout
Code and assets are kept apart, and both use the same sub-folders: one per page, plus `App`, `Shared` and a few for shared parts. No colours, styles, starting data or settings are written in code.

```
Workbench.Desktop/
  Code/
    App/        App startup (sign-in or main window), MainWindow (side panel, top bar), MainViewModel
    Account/    SignInWindow, SupabaseClient (Auth + REST, PKCE sign-in), Membership, GitLabClient
    Settings/   AccountMenu (side-panel footer and settings menu), ThemeManager, AppSettings
    Editor/ Map/ Compare/               valuation pages (MainViewModel.<Page>.cs each)
    Workspace/  RecordPage<T> (search, filter, form, save), models, WorkspaceViewModel, shared header and form parts
    Jobs/ Projects/ Board/ Tasks/ Clients/ Reports/ Requests/   workflow pages (WorkspaceViewModel.<Page>.cs each)
    Team/ Activity/ Intake/ Companies/  admin pages
    MockGitLab/ the demo GitLab and its window
    Shared/     helpers used by several files
  Assets/
    <same folders>/  styles (.xaml) and Colors.Light.xaml / Colors.Dark.xaml
    Account/    supabase.json, gitlab.json
    Editor/     DefaultRules.rules       Map/  map-style.json, study-area.json, cities.json, louisiana.geojson
    MockGitLab/ seed.json (the demo GitLab's starting issues)
```

All colours are `DynamicResource` look-ups from the colour files, so the theme switches while the app is open. `MainViewModel` (valuation) and `WorkspaceViewModel` (workflow) are each one class split into a file per page.

### Security
- **Isolation:** every workflow row belongs to one company. Row-level security lets people read and write only their own company's rows, and only once an admin has approved them.
- **Checked functions:** admin actions (approving, job titles, roles, adding people, companies, account managers) go through security-definer functions that check who is calling.
- **Self-promotion blocked:** column-level grants stop people from changing their own role or making themselves platform admin.
- **No deletes:** the app has no delete permission on any table.
- **Requests:** filing stamps who and when and always starts at New. Only the company's account manager (or a platform admin) can triage.
- **Tests:** the pgTAP tests in `supabase/tests` cover all of the above.

### CI/CD
- **Every push and pull request:**
  - Windows build plus engine tests;
  - a fresh local Supabase database with every migration applied, plus the security tests.
- **Merge to `main`:** applies new migrations to the hosted project with `supabase db push`, using the `SUPABASE_ACCESS_TOKEN` and `SUPABASE_DB_PASSWORD` repository secrets.

## What's placeholder
- **Parcels:** parcels and sales are made up (`SampleData.cs`), built so neighborhood N-04 starts out under-assessed.
- **Study area:** one area of Shreveport around Forest Park Cemetery, with its border traced from real OpenStreetMap streets.
- **Parcel shapes:** they're building footprints, not lot lines. Real parcel boundaries come from the parish assessor or GIS office.
- **Tax rates:** the millage of 120 is a sample rate. The 10% assessment ratio and the $7,500 homestead exemption follow Louisiana's residential rules.
- **Workflow data:** the sample clients, jobs and reports a new office starts with are invented.

## Next steps
- **Instant GitLab status:** a GitLab webhook into a Supabase Edge Function.
- **Roles:** developer and reviewer roles inside Levy.
- **Switching companies:** a company switcher for platform admins.
- **Real data:** real parcel shapes and sales, and the Louisiana Tax Commission's JSON tax-roll export.
- **Ratio study:** PRB and vertical-equity measures.
- **Scale:** 100k+ parcels, then profiling `Valuer.ValueAll`.

More detail: [docs/HANDOFF.md](docs/HANDOFF.md) (what was built and how) and [docs/GITLAB.md](docs/GITLAB.md).
