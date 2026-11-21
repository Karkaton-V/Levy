-- Workspace: organizations, members, profiles and the workflow tables (clients, jobs, projects, tasks, reports).
-- Every workflow row belongs to one organization; row-level security lets a user see only their organizations' rows.
-- A new sign-up gets a profile, an organization of their own and sample rows, so the app is never empty.

create schema if not exists private;
revoke all on schema private from public, anon;
grant usage on schema private to authenticated;

-- People and organizations ---------------------------------------------------------------------------------------

create table public.profiles (
    id uuid primary key references auth.users (id) on delete cascade,
    full_name text not null default '',
    email text not null default '',
    created_at timestamptz not null default now()
);

create table public.organizations (
    id uuid primary key default gen_random_uuid(),
    name text not null,
    created_at timestamptz not null default now()
);

create table public.organization_members (
    org_id uuid not null references public.organizations (id) on delete cascade,
    user_id uuid not null references public.profiles (id) on delete cascade,
    role text not null default 'member' check (role in ('owner', 'member')),
    created_at timestamptz not null default now(),
    primary key (org_id, user_id)
);
create index organization_members_user_id_idx on public.organization_members (user_id);

-- Workflow tables. Links between them carry org_id too, so a row can only point at rows in its own organization. ----

create table public.clients (
    id uuid primary key default gen_random_uuid(),
    org_id uuid not null references public.organizations (id) on delete cascade,
    name text not null,
    kind text not null default 'Individual' check (kind in ('Individual', 'Business', 'Trust', 'Government')),
    parcel_id text,
    city text,
    email text,
    phone text,
    created_at timestamptz not null default now(),
    unique (id, org_id)
);
create index clients_org_id_idx on public.clients (org_id);

create table public.jobs (
    id uuid primary key default gen_random_uuid(),
    org_id uuid not null references public.organizations (id) on delete cascade,
    client_id uuid,
    title text not null,
    job_type text not null default 'Appeal' check (job_type in ('Appeal', 'Reassessment', 'Exemption review', 'New construction', 'Ratio study')),
    status text not null default 'Not started' check (status in ('Not started', 'In progress', 'Waiting on client', 'In review', 'Completed')),
    due_date date,
    assignee_id uuid references public.profiles (id) on delete set null,
    budget_hours numeric(7, 2) not null default 0 check (budget_hours >= 0),
    actual_hours numeric(7, 2) not null default 0 check (actual_hours >= 0),
    created_at timestamptz not null default now(),
    unique (id, org_id),
    foreign key (client_id, org_id) references public.clients (id, org_id) on delete set null (client_id)
);
create index jobs_org_id_idx on public.jobs (org_id);
create index jobs_client_id_idx on public.jobs (client_id, org_id);
create index jobs_assignee_id_idx on public.jobs (assignee_id);

create table public.projects (
    id uuid primary key default gen_random_uuid(),
    org_id uuid not null references public.organizations (id) on delete cascade,
    name text not null,
    description text not null default '',
    status text not null default 'Open' check (status in ('Open', 'On hold', 'Closed')),
    start_date date,
    due_date date,
    owner_id uuid references public.profiles (id) on delete set null,
    progress integer not null default 0 check (progress between 0 and 100),
    created_at timestamptz not null default now(),
    unique (id, org_id)
);
create index projects_org_id_idx on public.projects (org_id);
create index projects_owner_id_idx on public.projects (owner_id);

create table public.tasks (
    id uuid primary key default gen_random_uuid(),
    org_id uuid not null references public.organizations (id) on delete cascade,
    project_id uuid,
    job_id uuid,
    title text not null,
    status text not null default 'Backlog' check (status in ('Backlog', 'To do', 'In progress', 'In review', 'Done')),
    priority text not null default 'Medium' check (priority in ('Low', 'Medium', 'High')),
    assignee_id uuid references public.profiles (id) on delete set null,
    due_date date,
    points integer not null default 0 check (points >= 0),
    sort_order integer not null default 0,
    created_at timestamptz not null default now(),
    foreign key (project_id, org_id) references public.projects (id, org_id) on delete set null (project_id),
    foreign key (job_id, org_id) references public.jobs (id, org_id) on delete set null (job_id)
);
create index tasks_org_id_idx on public.tasks (org_id);
create index tasks_project_id_idx on public.tasks (project_id, org_id);
create index tasks_job_id_idx on public.tasks (job_id, org_id);
create index tasks_assignee_id_idx on public.tasks (assignee_id);

create table public.reports (
    id uuid primary key default gen_random_uuid(),
    org_id uuid not null references public.organizations (id) on delete cascade,
    title text not null,
    kind text not null default 'Ratio study' check (kind in ('Ratio study', 'Appeal decision', 'Roll summary', 'Field review')),
    status text not null default 'Draft' check (status in ('Draft', 'In review', 'Closed')),
    author_id uuid references public.profiles (id) on delete set null,
    closed_at timestamptz,
    summary text not null default '',
    created_at timestamptz not null default now()
);
create index reports_org_id_idx on public.reports (org_id);
create index reports_author_id_idx on public.reports (author_id);

-- Membership checks used by the policies. Security definer so they can read organization_members without recursing into its own policy.

create function private.is_member(org uuid) returns boolean
language sql stable security definer set search_path = ''
as $$
    select exists (select 1 from public.organization_members m where m.org_id = org and m.user_id = (select auth.uid()));
$$;

create function private.shares_org(other uuid) returns boolean
language sql stable security definer set search_path = ''
as $$
    select exists (
        select 1 from public.organization_members mine
        join public.organization_members theirs on theirs.org_id = mine.org_id
        where mine.user_id = (select auth.uid()) and theirs.user_id = other);
$$;

revoke all on function private.is_member(uuid), private.shares_org(uuid) from public, anon;
grant execute on function private.is_member(uuid), private.shares_org(uuid) to authenticated;

-- Row-level security. Signed-out callers get nothing; rows are added and edited, not deleted, from the app. ---------

alter table public.profiles enable row level security;
alter table public.organizations enable row level security;
alter table public.organization_members enable row level security;
alter table public.clients enable row level security;
alter table public.jobs enable row level security;
alter table public.projects enable row level security;
alter table public.tasks enable row level security;
alter table public.reports enable row level security;

revoke all on public.profiles, public.organizations, public.organization_members,
    public.clients, public.jobs, public.projects, public.tasks, public.reports from anon;
revoke all on public.profiles, public.organizations, public.organization_members,
    public.clients, public.jobs, public.projects, public.tasks, public.reports from authenticated;
grant select, update on public.profiles, public.organizations to authenticated;
grant select on public.organization_members to authenticated;
grant select, insert, update on public.clients, public.jobs, public.projects, public.tasks, public.reports to authenticated;

create policy "Own profile and colleagues are visible" on public.profiles for select to authenticated
    using (id = (select auth.uid()) or private.shares_org(id));
create policy "Users edit their own profile" on public.profiles for update to authenticated
    using (id = (select auth.uid())) with check (id = (select auth.uid()));

create policy "Members see their organizations" on public.organizations for select to authenticated
    using (private.is_member(id));
create policy "Owners rename their organizations" on public.organizations for update to authenticated
    using (exists (select 1 from public.organization_members m where m.org_id = organizations.id and m.user_id = (select auth.uid()) and m.role = 'owner'))
    with check (exists (select 1 from public.organization_members m where m.org_id = organizations.id and m.user_id = (select auth.uid()) and m.role = 'owner'));

create policy "Members see their organizations' members" on public.organization_members for select to authenticated
    using (private.is_member(org_id));

create policy "Members read clients" on public.clients for select to authenticated using (private.is_member(org_id));
create policy "Members add clients" on public.clients for insert to authenticated with check (private.is_member(org_id));
create policy "Members edit clients" on public.clients for update to authenticated using (private.is_member(org_id)) with check (private.is_member(org_id));

create policy "Members read jobs" on public.jobs for select to authenticated using (private.is_member(org_id));
create policy "Members add jobs" on public.jobs for insert to authenticated with check (private.is_member(org_id));
create policy "Members edit jobs" on public.jobs for update to authenticated using (private.is_member(org_id)) with check (private.is_member(org_id));

create policy "Members read projects" on public.projects for select to authenticated using (private.is_member(org_id));
create policy "Members add projects" on public.projects for insert to authenticated with check (private.is_member(org_id));
create policy "Members edit projects" on public.projects for update to authenticated using (private.is_member(org_id)) with check (private.is_member(org_id));

create policy "Members read tasks" on public.tasks for select to authenticated using (private.is_member(org_id));
create policy "Members add tasks" on public.tasks for insert to authenticated with check (private.is_member(org_id));
create policy "Members edit tasks" on public.tasks for update to authenticated using (private.is_member(org_id)) with check (private.is_member(org_id));

create policy "Members read reports" on public.reports for select to authenticated using (private.is_member(org_id));
create policy "Members add reports" on public.reports for insert to authenticated with check (private.is_member(org_id));
create policy "Members edit reports" on public.reports for update to authenticated using (private.is_member(org_id)) with check (private.is_member(org_id));

-- Sample rows for a new organization: fictional Shreveport-area clients and work, dated around today. ----------------

create function private.seed_workspace(org uuid, owner uuid) returns void
language plpgsql security definer set search_path = ''
as $$
declare
    c uuid[] := array(select gen_random_uuid() from generate_series(1, 8));
    p uuid[] := array(select gen_random_uuid() from generate_series(1, 4));
    j uuid[] := array(select gen_random_uuid() from generate_series(1, 12));
    today date := current_date;
begin
    insert into public.clients (id, org_id, name, kind, parcel_id, city, email, phone) values
        (c[1], org, 'Marguerite Delacroix', 'Individual', '171406-021-0034', 'Shreveport', 'mdelacroix@example.com', '318-555-0101'),
        (c[2], org, 'Red River Tool & Die LLC', 'Business', '181309-004-0112', 'Bossier City', 'office@redrivertool.example.com', '318-555-0102'),
        (c[3], org, 'Pecan Bend Family Trust', 'Trust', '161512-030-0007', 'Keithville', 'trustee@pecanbend.example.com', '318-555-0103'),
        (c[4], org, 'Cross Lake Water District', 'Government', '171503-001-0001', 'Shreveport', 'records@crosslakewd.example.com', '318-555-0104'),
        (c[5], org, 'Calvin & Ruth Thibodeaux', 'Individual', '161616-012-0045', 'Greenwood', 'thibodeaux@example.com', '318-555-0105'),
        (c[6], org, 'Bayou Pierre Storage Co.', 'Business', '171409-018-0220', 'Shreveport', 'accounts@bayoupierre.example.com', '318-555-0106'),
        (c[7], org, 'Ouachita Pines Holdings Trust', 'Trust', '181211-007-0003', 'Haughton', 'admin@ouachitapines.example.com', '318-555-0107'),
        (c[8], org, 'Village of Blanchard', 'Government', '191402-002-0010', 'Blanchard', 'clerk@blanchard.example.com', '318-555-0108');

    insert into public.projects (id, org_id, name, description, status, start_date, due_date, owner_id, progress) values
        (p[1], org, '2027 roll reappraisal: District 3', 'Revalue the residential parcels in District 3 for the 2027 roll.', 'Open', today - 60, today + 120, owner, 35),
        (p[2], org, 'Commercial income model refresh', 'Update rents, vacancy, expense ratios and cap rates for the commercial income model.', 'Open', today - 30, today + 45, owner, 60),
        (p[3], org, 'Cross Lake shoreline remap', 'Redraw shoreline parcels from the new LiDAR survey.', 'On hold', today - 90, today + 90, owner, 20),
        (p[4], org, '2026 appeal season close-out', 'Close every 2026 appeal and file the decisions.', 'Closed', today - 200, today - 20, owner, 100);

    insert into public.jobs (id, org_id, client_id, title, job_type, status, due_date, assignee_id, budget_hours, actual_hours) values
        (j[1], org, c[1], 'Homestead appeal: 4410 Fairfield Ave', 'Appeal', 'In progress', today + 3, owner, 6, 4.5),
        (j[2], org, c[2], 'Industrial reassessment: plant expansion', 'Reassessment', 'Not started', today + 12, owner, 24, 0),
        (j[3], org, c[3], 'Agricultural use exemption review', 'Exemption review', 'Waiting on client', today + 5, owner, 8, 3),
        (j[4], org, c[4], 'Pump station exemption review', 'Exemption review', 'In review', today - 2, owner, 5, 5.5),
        (j[5], org, c[5], 'New construction: detached garage', 'New construction', 'Completed', today - 10, owner, 3, 2.5),
        (j[6], org, c[6], 'Self-storage income appeal', 'Appeal', 'In progress', today - 4, null, 12, 9),
        (j[7], org, c[7], 'Timberland reassessment: Haughton tract', 'Reassessment', 'In progress', today + 20, owner, 16, 6),
        (j[8], org, c[8], 'Municipal parcel ratio study', 'Ratio study', 'Not started', today + 30, null, 20, 0),
        (j[9], org, c[1], 'Ratio study: Fairfield neighborhood', 'Ratio study', 'In review', today + 6, owner, 10, 8),
        (j[10], org, c[2], 'Equipment rendition appeal', 'Appeal', 'Waiting on client', today + 1, null, 6, 1),
        (j[11], org, c[6], 'New construction: Building C', 'New construction', 'Not started', today + 14, owner, 4, 0),
        (j[12], org, c[5], 'Homestead exemption renewal', 'Exemption review', 'Completed', today - 25, owner, 2, 1.5);

    insert into public.tasks (org_id, project_id, job_id, title, status, priority, assignee_id, due_date, points, sort_order) values
        (org, p[1], null, 'Pull 2026 sales file for District 3', 'Done', 'High', owner, today - 15, 3, 0),
        (org, p[1], null, 'Remove non-arm''s-length sales', 'In progress', 'High', owner, today + 2, 5, 0),
        (org, p[1], null, 'Build land value curves', 'To do', 'Medium', owner, today + 10, 8, 0),
        (org, p[1], null, 'Field-check 40 outlier parcels', 'Backlog', 'Medium', null, today + 25, 5, 0),
        (org, p[1], null, 'Draft District 3 ratio memo', 'Backlog', 'Low', null, today + 40, 3, 1),
        (org, p[2], null, 'Collect rent rolls from 12 retail centers', 'In progress', 'High', owner, today - 1, 5, 1),
        (org, p[2], null, 'Update cap-rate table', 'To do', 'High', owner, today + 7, 3, 1),
        (org, p[2], null, 'Check expense ratios against 2025', 'In review', 'Medium', owner, today + 3, 3, 0),
        (org, p[2], null, 'Publish model change log', 'Backlog', 'Low', null, today + 30, 2, 2),
        (org, p[3], null, 'Request LiDAR tiles from parish GIS', 'Done', 'Medium', owner, today - 40, 2, 1),
        (org, p[3], null, 'Redraw 18 shoreline parcels', 'Backlog', 'Medium', null, today + 60, 8, 3),
        (org, null, j[1], 'Schedule the appeal hearing', 'To do', 'High', owner, today + 2, 1, 2),
        (org, null, j[1], 'Prepare comparable sales grid', 'In progress', 'High', owner, today + 1, 3, 2),
        (org, null, j[3], 'Request the lease agreement from the owner', 'In review', 'Medium', owner, today + 4, 1, 1),
        (org, null, j[6], 'Reconcile the income approach', 'In progress', 'Medium', null, today - 3, 5, 3),
        (org, null, j[9], 'Run COD and PRD for Fairfield', 'Done', 'Medium', owner, today - 5, 2, 2);

    insert into public.reports (org_id, title, kind, status, author_id, closed_at, summary) values
        (org, '2026 residential ratio study: Shreveport', 'Ratio study', 'Closed', owner, now() - interval '30 days', 'Median ratio 0.96, COD 9.8, PRD 1.01. Within IAAO standards for residential property.'),
        (org, 'Appeal decision: 4410 Fairfield Ave', 'Appeal decision', 'Draft', owner, null, 'Comparable sales support a reduction of about 6% in the assessed value.'),
        (org, '2026 roll summary: Caddo Parish', 'Roll summary', 'Closed', owner, now() - interval '60 days', 'Taxable value up 4.2% on the year, led by new construction in south Shreveport.'),
        (org, 'Field review: Cross Lake shoreline', 'Field review', 'In review', owner, null, 'Eighteen parcels need new boundaries after the LiDAR survey.'),
        (org, 'Appeal decision: Bayou Pierre Storage', 'Appeal decision', 'Closed', owner, now() - interval '12 days', 'Income approach upheld; the assessed value stands.'),
        (org, 'Commercial ratio study: third quarter', 'Ratio study', 'Draft', owner, null, 'Sales sample still being cleaned.');
end;
$$;

-- On sign-up: profile, an organization of their own with them as owner, and sample rows. ----------------------------

create function private.handle_new_user() returns trigger
language plpgsql security definer set search_path = ''
as $$
declare
    display_name text := coalesce(nullif(trim(new.raw_user_meta_data ->> 'full_name'), ''), nullif(split_part(coalesce(new.email, ''), '@', 1), ''), 'New user');
    org uuid;
begin
    insert into public.profiles (id, full_name, email) values (new.id, display_name, coalesce(new.email, ''));
    insert into public.organizations (name) values (display_name || '''s office') returning id into org;
    insert into public.organization_members (org_id, user_id, role) values (org, new.id, 'owner');
    perform private.seed_workspace(org, new.id);
    return new;
end;
$$;

revoke all on function private.seed_workspace(uuid, uuid), private.handle_new_user() from public, anon, authenticated;

create trigger on_auth_user_created
    after insert on auth.users
    for each row execute function private.handle_new_user();
