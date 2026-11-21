# GitLab: sign-in and issue intake

How Levy connects to a self-managed GitLab, and the setup that has to be done by hand on GitLab and in Supabase.

## The workflow

1. **Client staff report it.** Someone at an assessor's office presses **Report an issue** on the Requests page. Levy fills in where they were (page and parcel). They don't need a GitLab account. A client can also tell their account manager, who files it the same way.
2. **The account manager triages it.** On the Intake page they see requests from every company they manage. They set the kind, priority and codebase (Rails or .NET), add a note, and press **Send to GitLab**.
3. **Levy opens the GitLab issue** in that codebase's project, as the account manager. The issue gets these labels:
   - `type::bug` / `type::feature` / `type::question`
   - `priority::low` … `priority::critical`
   - `stack::rails` / `stack::.net`
   - `client::<company>`
   - `source::levy`

   Developers pull from an issue board, using the `priority::` lists, and filter by client or codebase.
4. **Developers work it in GitLab:** branch, open a merge request whose description says `Closes #<issue>`, then the pipeline runs and the review happens.
5. **Levy follows it.** Opening Intake, or pressing **Refresh from GitLab**, reads each open issue and its merge requests, then updates the request's status. The client sees it on their Requests page:

   | GitLab | Levy status |
   |---|---|
   | Issue open, nobody on it | In GitLab |
   | Assigned, a `doing` / `in progress` label, or a draft merge request | In progress |
   | An open, non-draft merge request | In review |
   | Issue closed by a merged merge request | Fixed |
   | Issue closed without a merge | Closed |

Every filing, triage change and hand-off to GitLab is written to the company's Activity log.

## Demo mode (no GitLab server needed)

`Assets/Account/gitlab.json` ships with `"mock": true`, so Levy uses a **mock GitLab** kept on this PC instead of a real server:
- **Where it lives:** `%LOCALAPPDATA%\ValuationWorkbench\mock-gitlab.json`, started from `Assets/MockGitLab/seed.json`. Nothing is sent anywhere, and no OAuth app or Supabase change is needed.
- **Sign-in:** people sign in with email. **Sign in with GitLab** explains that GitLab is in demo mode.
- **Intake:** it is always "connected". **Send to GitLab** opens the issue in the mock, and the mock's changes refresh Intake straight away. **Open mock GitLab** or an issue link opens the **Mock GitLab** window.
- **The window:**
  - **Issues:** a list sorted by priority label, which is what developers pull from.
  - **Actions:** Assign to me, Start work (adds `workflow::doing`), Create merge request (a draft on a branch named after the issue), Push commit (runs a pipeline for about 2 s, with a "Next pipeline fails" switch), Mark ready, Approve, Merge.
  - **Acting as:** switches between the seed's developers and reviewers.
- **Review rules, as GitLab projects usually set them:**
  - the pipeline must pass;
  - drafts can't merge;
  - one approval is needed;
  - authors can't approve their own merge request;
  - new commits reset approvals.

  Merging closes the issue, and Intake marks the request **Fixed**.
- **Reset demo** starts the mock over from the seed.

To go live, set `"mock": false`, point `url` and `projects` at the real server, and do the one-time setup below.

## Who is who

- **Client staff:** members of a client company. They sign in with email and password (or a company code), and see only their own company's requests.
- **Platform staff:** platform admins (`profiles.is_platform_admin`, set in the database). They sign in with GitLab.
  - A platform admin is set as a company's **account manager** on the Companies page.
  - Account managers see that company's requests on Intake; platform admins see every company's.
- **New GitLab sign-ins** get a profile (with their GitLab username) but no office of their own. A company admin or platform admin adds them to a company by email.

## One-time setup

### 1. Register Levy on your GitLab server

On the agency's GitLab, register an OAuth application:
- For the whole instance: **Admin area → Applications**.
- For one group: the group's **Settings → Applications**.

Settings:
- **Name:** Levy
- **Redirect URI:** `https://hrzalbzhskbjninplrlu.supabase.co/auth/v1/callback`
- **Confidential:** yes
- **Scopes:** `api`, `read_user`

Keep the **Application ID** and **Secret** for the next step.

### 2. Turn on GitLab sign-in in Supabase

In the Supabase dashboard for project `hrzalbzhskbjninplrlu`:
- **Authentication → Sign In / Providers → GitLab:**
  - Turn it on.
  - Paste the Application ID and Secret.
  - Set **GitLab URL** to your server, e.g. `https://gitlab.youragency.gov` (leave it empty for gitlab.com).
- **Authentication → URL Configuration → Redirect URLs:** add `http://localhost:53682/gitlab/`. This is where the browser hands the sign-in back to the desktop app.

### 3. Point Levy at your GitLab

Edit `src/Workbench.Desktop/Assets/Account/gitlab.json`:

```json
{
  "url": "https://gitlab.youragency.gov",
  "callback_port": 53682,
  "scopes": "api read_user",
  "projects": {
    "Rails": "assessors/smartcama",
    ".NET": "assessors/levy"
  }
}
```

- `url` must match the GitLab URL set in Supabase.
- `projects` maps each codebase to its GitLab project path.
- If you change `callback_port`, change the redirect URL in Supabase too.

### 4. Set up review in GitLab

Configure these in each project, under **Settings → Merge requests**:
- **Pipelines must succeed.**
- **All threads must be resolved.**
- **Merge request approvals:** require at least one approval. On GitLab Premium or Ultimate, add approval rules and a `CODEOWNERS` file, so Rails changes go to Rails reviewers and .NET changes to .NET reviewers.
- **Merge request description template** (`.gitlab/merge_request_templates/Default.md`): include `Closes #` and a test checklist.

GitLab creates the `type::` / `priority::` / `stack::` / `client::` labels the first time Levy uses them. Make an issue board with one list per `priority::` label.

## Notes

- **Tokens:** Levy acts in GitLab as the signed-in account manager, using the token from **Sign in with GitLab**. GitLab tokens last about two hours; after that, Intake shows **Connect GitLab**, which signs in again as the same person.
- **Supabase answers:** if GitLab sign-in isn't set up in Supabase yet, both buttons say so instead of opening the browser.
- **Status updates:** they come from Intake (on open, or with **Refresh from GitLab**). A GitLab webhook into a Supabase Edge Function would make them instant, which is the next step.
- **Federal work:** check whether hosted Supabase is allowed for the contract's data. Federal deployments usually need a FedRAMP-authorized or self-hosted setup.
