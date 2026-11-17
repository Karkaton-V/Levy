# Handoff: workspace, accounts, themes and CI

Started in a claude.ai chat on 2026-11-17 and moved to Claude Code. Branch: `workspace-accounts`.

## What the owner asked for
1. A settings (gear) button in the bottom-left of the navigation rail that opens a popup upward, with a light/dark mode switch.
2. More pages in the navigation rail for workflow: job listings, open projects, a scrum board, a client list, open tasks, closed reports.
3. User accounts: registration and sign-in against the project's Supabase.
4. CI/CD on GitHub Actions tied to the Supabase project.

Their reference apps were Karbon, Taxfiler, TaxDome and Canopy: a dark left rail, light content, summary counters above tables, status chips, sortable tables.

## Decisions already made with the owner
- CI/CD: every push and PR builds the solution and runs the tests; a merge to `main` applies `supabase/migrations` to the hosted project.
- Sign-in screen first: the app opens to Sign in / Create account, and the session is remembered between launches.
- New pages read and write real Supabase tables, and each user sees only their organization's data.
- Pages support view, search, filter, sort, add and edit (no delete was asked for). Scrum board cards drag between columns.

## Done on this branch
- Blank page fixed. The page `TabControl` had no items host, so it never selected or showed a page. `PageTabs` in `Assets/App/AppStyles.xaml` now keeps a hidden, zero-height `TabPanel`.
- Themes:
  - Every colour moved to `Assets/{Shared,App,Editor,Map,Compare}/Colors.Light.xaml` and `Colors.Dark.xaml`, and all references are `DynamicResource`.
  - `Code/Settings/ThemeManager.cs` swaps the colour files at runtime.
  - `Code/Settings/AppSettings.cs` saves the choice to `%LOCALAPPDATA%\ValuationWorkbench\settings.json`, and `App.OnStartup` applies it.
  - The syntax colours are `Assets/Shared/Rules.Light.xshd` and `Rules.Dark.xshd`, and the editors re-apply them on theme change.
  - There is no UI to switch themes yet; that is the settings popup below.
- Shared control styles in `Assets/Shared/Theme.xaml` for the coming pages: `Input` (Tag = placeholder), `SearchInput`, `PasswordInput`, `Select` (ComboBox) and `SelectItem`, `Table` (DataGrid) with `GridHeader`, `GridRow` and `GridCell`, `Chip` and `ChipText`, `FilterChip`, `IconButton`, `Progress`, `FormLabel`, `ErrorText` and `DueText`, plus slim scrollbars.
  - `Chip` and `ChipText` bind to an object with `Text` and `Tone`, where Tone is Blue, Green, Amber, Red, Purple or Grey.
  - `DueText` turns red when `IsOverdue` is true.
- `Assets/Account/supabase.json` holds the project URL and publishable key.
- Supabase project `hrzalbzhskbjninplrlu`: the `public` schema was empty on 2026-11-17.

None of this has been run on Windows yet. The XAML parses, every resource key resolves, and the non-WPF code type-checks.

## Progress (2026-11-17, branch `claude/supabase-mcp-setup-5b491a`)
- Step 1 done: `Code/Settings/AccountMenu.xaml` is the rail footer and settings menu. The gear spins one turn and the menu slides up from behind the footer; `SignOutRequested` shows the Sign out button once step 3 handles it, and `UserName` / `Email` fill the footer.
- Step 2 done: `supabase/migrations/20261117164716_workspace.sql` was applied to the hosted project with the Supabase MCP (it is recorded in the migration history, so `supabase db push` skips it). Security advisors are clean; the performance advisor only lists unused indexes on the empty tables. Sign-up seeding, organization isolation, blocked deletes and signed-out access were tested in a rolled-back transaction.
- `.mcp.json` adds the Supabase MCP server for this project.

## Still to do, in order

### 1. Settings popup (owner's first request)
- Rail footer in `Code/App/MainWindow.xaml`: replace the study-area block with the signed-in user (initials avatar, name, email) and a gear `IconButton` (glyph `&#xE713;`) on the right.
- The gear toggles a `Popup` with `Placement="Top"`, `PlacementTarget` set to the footer, `StaysOpen="False"`, `AllowsTransparency="True"` and `PopupAnimation="Slide"`, so it opens upward. The popup is a `Card` holding:
  - account name and email;
  - an Appearance switch: two `SegmentButton`s, Light and Dark, inside a `SegmentTrackBorder`, which call `ThemeManager.Apply`;
  - a Sign out button.
- Put it in `Code/Settings/` (for example `AccountMenu.xaml`), with any styles in `Assets/App/AppStyles.xaml`.

### 2. Database (`supabase/migrations/<timestamp>_workspace.sql`)
- Tables, each with `id uuid default gen_random_uuid()`, `org_id`, `created_at` and an index on `org_id`:
  - `profiles` (id references `auth.users`, full_name, email)
  - `organizations` (name)
  - `organization_members` (org_id, user_id, role; primary key on both)
  - `clients` (name, kind: Individual / Business / Trust / Government, parcel_id, city, email, phone)
  - `jobs` (client_id, title, job_type: Appeal / Reassessment / Exemption review / New construction / Ratio study, status: Not started / In progress / Waiting on client / In review / Completed, due_date, assignee_id, budget_hours, actual_hours)
  - `projects` (name, description, status: Open / On hold / Closed, start_date, due_date, owner_id, progress 0 to 100)
  - `tasks` (project_id, job_id, title, status: Backlog / To do / In progress / In review / Done, priority: Low / Medium / High, assignee_id, due_date, points, sort_order)
  - `reports` (title, kind: Ratio study / Appeal decision / Roll summary / Field review, status: Draft / In review / Closed, author_id, closed_at, summary)
- Security:
  - Turn on row-level security on every table.
  - `private.is_member(org uuid)` is a `security definer` function with `search_path = ''` that checks `organization_members` against `(select auth.uid())`.
  - Each workspace table gets policies for authenticated users, `using (private.is_member(org_id)) with check (private.is_member(org_id))`.
  - Users can see their own profile and the profiles of people in their organizations.
- Trigger on `auth.users` insert:
  - create the profile from `raw_user_meta_data->>'full_name'`;
  - create an organization "<name>'s office" with the user as owner;
  - call `private.seed_workspace(org, user)`, which adds sample rows: about 8 clients, 12 jobs, 4 projects, 16 tasks and 6 reports with fictional Shreveport-area names, so a new account is never empty.
- Add a minimal `supabase/config.toml` (`project_id = "levy"`, `[db] major_version = 17`).
- Run the Supabase advisors after applying.

### 3. Sign-in and the Supabase client (`Code/Account/`)
- `SupabaseClient.cs`: plain `HttpClient` against GoTrue and PostgREST, with `apikey` and Bearer headers and snake_case JSON (`JsonNamingPolicy.SnakeCaseLower`).
  - Auth: `POST /auth/v1/signup` (sends `data.full_name`; with email confirmation on, it returns no session, so tell the user to check their inbox), `POST /auth/v1/token?grant_type=password`, `grant_type=refresh_token` (refresh within 60 s of expiry) and `POST /auth/v1/logout`.
  - REST: `GET /rest/v1/{table}?select=*`, `POST` with `Prefer: return=representation`, and `PATCH ?id=eq.{id}`.
  - Write dates as `yyyy-MM-dd`.
- Store the refresh token with DPAPI (`ProtectedData`, CurrentUser scope) in `%LOCALAPPDATA%\ValuationWorkbench\session.bin`.
- `SignInWindow.xaml`: a dark brand panel on the left and a form on the right, with a Sign in / Create account switch, name (create only), email and password, plus an error or info line. Styles go in `Assets/Account/`.
- `App.OnStartup` sets `ShutdownMode = OnExplicitShutdown`, tries to restore the session, then shows `SignInWindow` or `MainWindow`. Sign out returns to `SignInWindow`.

### 4. Workflow pages
- One folder per page under `Code/` (`Jobs`, `Projects`, `Board`, `Tasks`, `Clients`, `Reports`), plus `Code/Workspace/` for the models and a `WorkspaceViewModel`:
  - The models are mutable classes with `Copy()` and `[JsonIgnore]` display fields (client and assignee names, `StatusChip`, `PriorityChip`, `IsOverdue`).
  - `WorkspaceViewModel` is split into partials per page, like `MainViewModel`.
  - A generic `RecordPage<T>` holds the items, a filtered `ICollectionView`, Search, one dropdown filter, "Assigned to me", the selection, the edit draft, IsNew / IsSaving / Error, the New / Edit / Save / Cancel commands, and a row of counters.
- Each page has:
  - a row of counters using `SummaryCard`, for example Jobs: due this week, overdue, in progress, waiting on client;
  - a toolbar with search, a filter, "Assigned to me" and a New button;
  - a `Table` DataGrid with chips and sortable columns;
  - an edit form card on the right using `Input`, `Select` and `FormLabel`. Pickers use an options list whose first entry is "Unassigned", and dates are an `Input` bound with `StringFormat=yyyy-MM-dd`.
- Scrum board: five status columns of task cards, dragged between columns with `DragDrop` to patch the task's status. Clicking a card opens the same task form as the Tasks page.
- Navigation rail:
  - Add a VALUATION heading over Rules, Parcel map and Compare runs, and a WORKFLOW heading over Jobs, Projects, Scrum board, Tasks, Clients and Reports.
  - Headings are disabled `ListBoxItem`s, so they can't be selected.
  - Switch pages from code-behind by item name rather than by index.
  - Hide the summary strip and the Run button on workflow pages, and show the organization name as the subtitle.

### 5. CI/CD (`.github/workflows/ci.yml`)
- Job `build`: `windows-latest`, `actions/setup-dotnet@v4` (8.0.x), `dotnet build ValuationWorkbench.sln -c Release`, then `dotnet test tests/Workbench.Engine.Tests -c Release`. Runs on every push and PR.
- Job `database-check`: `ubuntu-latest`, `supabase/setup-cli@v1`, then `supabase db start`, which applies every migration to a local Postgres and catches SQL errors before merge. Runs on PRs.
- Job `deploy-database`: runs on push to `main` after `build`. It runs `supabase link --project-ref hrzalbzhskbjninplrlu`, then `supabase db push`, with env `SUPABASE_ACCESS_TOKEN` and `SUPABASE_DB_PASSWORD`.
- The owner has to add those two repository secrets in GitHub (Settings → Secrets and variables → Actions).

### 6. Finish
Update `README.md` (accounts, pages, themes, CI), push the branch and open a PR to `main`.
