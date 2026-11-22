---
layout: page
title: Architecture
permalink: /architecture/
---

## Projects

| Part | What it is |
|---|---|
| `src/Workbench.Engine` | The rules parser, the cost-approach valuer with its trace, the ratio study and the sample parcels. Plain .NET 8, no UI. |
| `src/Workbench.Desktop` | The WPF app (assembly `ValuationWorkbench`, namespace `Workbench.Desktop`). AvalonEdit for the rules editor, Mapsui and OpenStreetMap for the map. |
| `tests/Workbench.Engine.Tests` | xUnit tests for the engine. |
| `supabase/migrations` | The database: tables, row-level security, functions, triggers and Storage. |
| `supabase/tests/database` | pgTAP tests for the database rules. |
| `.github/workflows` | CI/CD and this site. |

The engine knows nothing about WPF or Supabase, so it builds and tests on any OS.

## The desktop app's layout
Code and assets are kept apart, and both use the same sub-folders: `App`, `Shared`, then one folder per page.

```
Workbench.Desktop/
  Code/
    App/        startup (sign-in or main window), MainWindow, MainViewModel
    Account/    SignInWindow, SupabaseClient, Membership, GitLabClient
    Settings/   AccountMenu, profile card, Edit profile, ThemeManager, AppSettings
    Editor/ Map/ Compare/                                     valuation pages
    Workspace/  RecordPage<T>, models, WorkspaceViewModel, shared page parts
    Jobs/ Projects/ Board/ Tasks/ Clients/ Reports/ Requests/ workflow pages
    Team/ Activity/ Intake/ Companies/                        admin pages
    MockGitLab/ the demo GitLab and its window
    Shared/     helpers used by several files (Shared.cs)
  Assets/
    <same folders>/  styles (.xaml) and Colors.Light.xaml / Colors.Dark.xaml
    Account/    supabase.json, gitlab.json
    Editor/     DefaultRules.rules, code-themes.json, Rules.*.xshd
    Map/        map-style.json, study-area.json, cities.json, louisiana.geojson
    Settings/   badges.json, company-tags.json
    MockGitLab/ seed.json
```

**The rules:**
- No colours, styles, starting data or settings are written in `Code/`.
- Styles are `.xaml` files in `Assets/<folder>/`. Each style dictionary merges `Assets/Shared/Theme.xaml` itself, and styles and fonts are used with `{StaticResource}`.
- Data files (`.json`, `.geojson`, `.xshd`, `.rules`) are copied to the output folder and read through `Shared.AssetPath` / `Shared.ReadAsset`.
- Colours live only in `Colors.Light.xaml` and `Colors.Dark.xaml`, with the same keys in both, and are always used with `{DynamicResource}`.
- `MainViewModel` (valuation) and `WorkspaceViewModel` (workflow) are each one class split into partial files, one per page folder (for example `MainViewModel.Editor.cs`, `WorkspaceViewModel.Jobs.cs`).
- Helpers used by several files go in `Code/Shared/Shared.cs` rather than many small utility files.

## Themes: ThemeManager
`Code/Settings/ThemeManager.cs` swaps the light and dark colour dictionaries while the app is open. Because every colour is a `DynamicResource`, the whole window repaints at once. It also applies the profile colour by shifting the blue-tinted keys to that hue.

AvalonEdit copies colours rather than binding them, so the editor listens for `ThemeManager.Changed` and reloads its syntax file: `Rules.Light.xshd` / `Rules.Dark.xshd` for "Match the app", or the fixed `Rules.Midnight`, `Ember`, `Forest` or `Paper` schemes.

## The database
Supabase Postgres, changed only through files in `supabase/migrations/`, applied in order:

| Migration | What it adds |
|---|---|
| `…_workspace.sql` | Profiles, organizations, members, clients, jobs, projects, tasks, reports; row-level security; the sign-up trigger and sample data. |
| `…_admin_review.sql`, `…_admin_review_anon.sql` | Roles, member status, job titles, join codes and the admin functions; the signed-out code check. |
| `…_activity_log.sql` | The activity table and its triggers. |
| `…_gitlab_intake.sql` | GitLab profiles, account managers, `issue_reports` and the intake functions. |
| `…_profiles_badges.sql` | Profile fields, the `profile-media` bucket, badges and `delete_account`. |
| `…_company_tags.sql` | Company tags and the pronoun choices. |
| `…_code_theme.sql` | `profiles.code_theme`. |

Tables are reached through PostgREST with the signed-in user's token; anything that needs more than row-level security is a security-definer function with an empty `search_path` that checks the caller. A new schema change is a new migration file; it reaches the hosted project through CI (see [CI/CD](ci-cd.md)).
