---
layout: page
title: Workflow pages
permalink: /workflow/
---

The workflow pages hold one company's shared data in Supabase. Everyone sees only their own company's rows (see [Accounts and security](accounts.md)). The side panel groups them under WORKFLOW and ADMIN.

## Records: Jobs, Projects, Tasks, Clients, Reports
These five pages share one generic page (`RecordPage<T>`), so they work the same way:
- counters along the top (for example, Jobs shows due this week, overdue, in progress and waiting on client);
- search, a status filter and "Assigned to me";
- a sortable table with status chips and red overdue dates;
- an edit form beside the table, with Save and Cancel (Cancel throws the draft away).

There's no delete: the app has no delete permission on any table.

| Page | What a row is |
|---|---|
| **Jobs** | A piece of client work: appeal, reassessment, exemption review, new construction or ratio study, with a due date, assignee and hours. |
| **Projects** | Longer work with an owner, dates and progress. |
| **Tasks** | Steps within a project or job, with priority, points and status. |
| **Clients** | Individuals, businesses, trusts and government bodies, with a parcel and contact details. |
| **Reports** | Ratio studies, appeal decisions, roll summaries and field reviews, from draft to closed. |

## Scrum board
Tasks in five columns: Backlog, To do, In progress, In review and Done. Dragging a card to another column saves its new status. Clicking a card opens the same task form as the Tasks page.

## Requests and GitLab intake
- **Requests** (everyone): report a bug or ask for a feature. Levy records the page and parcel you were on. You follow the request through to **Fixed** without needing a GitLab account.
- **Intake** (account managers): requests from every company they manage. They set the kind, priority, codebase (Rails or .NET) and a note, then press **Send to GitLab**. The issue gets `type::`, `priority::`, `stack::`, `client::` and `source::levy` labels.
- **Status back from GitLab:** Levy reads each issue, its merge requests and their pipelines, and shows In GitLab, In progress, In review, Fixed or Closed.
- **Demo mode:** `Assets/Account/gitlab.json` starts with `"mock": true`, so a mock GitLab on your PC stands in for a server. In its window you play developer and reviewer: assign, start work, open a draft merge request, run the pipeline, mark it ready, approve and merge. The usual rules hold: the pipeline must pass, drafts can't merge, and authors can't approve their own merge request.

The full workflow and the one-time setup for a real GitLab are in the repository's `docs/GITLAB.md`.

## Team (company admins)
- Approve or decline people who joined with the company code.
- Set job titles (shown under the logo), roles (admin or member) and access, and add existing accounts by email. A company always keeps at least one active admin.
- Award badges with a reason, and take them back.
- Set the company tag (see [Accounts and security](accounts.md#company-tags)).
- Delete the account of someone who works only under you, after a confirmation. Their work stays, unassigned.

## Activity (company admins)
A read-only log of who did what, to which record, and when. Database triggers write it on clients, jobs, projects, tasks, reports, members and requests, so the app can't skip or forge an entry, and nobody can edit it. The page shows the latest 500 entries and filters by kind.

## Companies (platform admins)
Create and rename companies, add people, and choose each company's account manager (the person whose Intake page gets its requests).
