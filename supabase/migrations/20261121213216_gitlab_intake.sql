-- GitLab sign-in and issue intake: staff sign in with the agency's GitLab; client staff file requests in Levy;
-- each company's account manager triages them and sends them to GitLab, where developers pick them up.

-- People who sign in with GitLab: remember their GitLab username; they join a company when an admin adds them. ----------

alter table public.profiles add column gitlab_username text;

create or replace function private.handle_new_user() returns trigger
language plpgsql security definer set search_path = ''
as $$
declare
    display_name text := coalesce(nullif(trim(new.raw_user_meta_data ->> 'full_name'), ''), nullif(trim(new.raw_user_meta_data ->> 'name'), ''),
                                  nullif(split_part(coalesce(new.email, ''), '@', 1), ''), 'New user');
    code text := nullif(upper(trim(new.raw_user_meta_data ->> 'company_code')), '');
    via_gitlab boolean := coalesce(new.raw_app_meta_data ->> 'provider', '') = 'gitlab';
    org uuid;
begin
    insert into public.profiles (id, full_name, email, gitlab_username)
    values (new.id, display_name, coalesce(new.email, ''),
            case when via_gitlab then coalesce(new.raw_user_meta_data ->> 'user_name', new.raw_user_meta_data ->> 'preferred_username') end);
    if code is not null then
        select o.id into org from public.organizations o where o.join_code = code;
        if org is null then raise exception 'No company uses the code %.', code; end if;
        insert into public.organization_members (org_id, user_id, role, status) values (org, new.id, 'member', 'pending');
    elsif not via_gitlab then
        insert into public.organizations (name) values (display_name || '''s office') returning id into org;
        insert into public.organization_members (org_id, user_id, role, status, decided_at) values (org, new.id, 'admin', 'active', now());
        perform private.seed_workspace(org, new.id);
    end if;
    return new;
end;
$$;

-- Account managers: each company has one on the vendor side, chosen by a platform admin. ---------------------------

alter table public.organizations add column account_manager_id uuid references public.profiles (id) on delete set null;
create index organizations_account_manager_id_idx on public.organizations (account_manager_id);

-- Who may triage a company's requests: its account manager, or any platform admin.
create function private.can_triage(org uuid) returns boolean
language sql stable security definer set search_path = ''
as $$
    select private.is_platform_admin()
        or exists (select 1 from public.organizations o where o.id = org and o.account_manager_id = (select auth.uid()));
$$;

create function private.is_account_manager() returns boolean
language sql stable security definer set search_path = ''
as $$ select exists (select 1 from public.organizations o where o.account_manager_id = (select auth.uid())); $$;

revoke all on function private.can_triage(uuid), private.is_account_manager() from public, anon;
grant execute on function private.can_triage(uuid), private.is_account_manager() to authenticated;

-- my_memberships also says whether the caller manages any accounts, and who their company's account manager is.
drop function public.my_memberships();
create function public.my_memberships()
returns table (org_id uuid, org_name text, role text, status text, job_title text, join_code text, is_platform_admin boolean, joined_at timestamptz,
               is_account_manager boolean, account_manager_name text)
language sql stable security definer set search_path = ''
as $$
    select m.org_id, o.name, m.role, m.status, m.job_title,
           case when m.role = 'admin' and m.status = 'active' then o.join_code end,
           private.is_platform_admin(), m.created_at, private.is_account_manager(),
           coalesce((select p.full_name from public.profiles p where p.id = o.account_manager_id), '')
    from public.organization_members m join public.organizations o on o.id = m.org_id
    where m.user_id = (select auth.uid())
    order by (m.status = 'active') desc, m.created_at;
$$;

-- Companies list and save now carry the account manager.
drop function public.save_company(uuid, text, boolean);
drop function public.list_companies();

create function public.list_companies()
returns table (id uuid, name text, join_code text, members integer, pending integer, admins integer, created_at timestamptz,
               account_manager_id uuid, account_manager_name text)
language plpgsql stable security definer set search_path = ''
as $$
begin
    if not private.is_platform_admin() then raise exception 'Only a platform admin can see every company.' using errcode = '42501'; end if;
    return query
        select o.id, o.name, o.join_code,
               (select count(*)::integer from public.organization_members m where m.org_id = o.id and m.status = 'active'),
               (select count(*)::integer from public.organization_members m where m.org_id = o.id and m.status = 'pending'),
               (select count(*)::integer from public.organization_members m where m.org_id = o.id and m.status = 'active' and m.role = 'admin'),
               o.created_at, o.account_manager_id,
               coalesce((select p.full_name from public.profiles p where p.id = o.account_manager_id), '')
        from public.organizations o
        order by o.name;
end;
$$;

create function public.save_company(company uuid, company_name text, with_sample_data boolean, manager uuid)
returns table (id uuid, name text, join_code text, members integer, pending integer, admins integer, created_at timestamptz,
               account_manager_id uuid, account_manager_name text)
language plpgsql security definer set search_path = ''
as $$
declare
    org uuid := company;
begin
    if not private.is_platform_admin() then raise exception 'Only a platform admin can create or change companies.' using errcode = '42501'; end if;
    if nullif(trim(company_name), '') is null then raise exception 'Give the company a name.'; end if;
    if manager is not null and not exists (select 1 from public.profiles p where p.id = manager and p.is_platform_admin) then
        raise exception 'Account managers come from the platform staff.';
    end if;
    if org is null then
        insert into public.organizations (name, account_manager_id) values (trim(company_name), manager) returning organizations.id into org;
        if with_sample_data then perform private.seed_workspace(org, (select auth.uid())); end if;
    else
        update public.organizations o set name = trim(company_name), account_manager_id = manager where o.id = org;
        if not found then raise exception 'That company no longer exists.'; end if;
    end if;
    return query select * from public.list_companies() c where c.id = org;
end;
$$;

-- The people who can be account managers (platform staff), for the Companies page picker.
create function public.list_staff()
returns table (id uuid, full_name text, email text, gitlab_username text)
language plpgsql stable security definer set search_path = ''
as $$
begin
    if not private.is_platform_admin() then raise exception 'Only a platform admin can see the staff list.' using errcode = '42501'; end if;
    return query select p.id, p.full_name, p.email, p.gitlab_username from public.profiles p where p.is_platform_admin order by p.full_name;
end;
$$;

-- Requests: bugs and feature requests from a company, triaged by its account manager and tracked in GitLab. ----------

create table public.issue_reports (
    id uuid primary key default gen_random_uuid(),
    org_id uuid not null references public.organizations (id) on delete cascade,
    reported_by uuid references public.profiles (id) on delete set null,
    reporter_name text not null default '',
    title text not null check (length(trim(title)) > 0),
    kind text not null default 'Bug' check (kind in ('Bug', 'Feature', 'Question')),
    description text not null default '',
    context text not null default '',
    status text not null default 'New' check (status in ('New', 'Triaged', 'In GitLab', 'In progress', 'In review', 'Fixed', 'Closed', 'Declined')),
    priority text check (priority in ('Low', 'Medium', 'High', 'Critical')),
    stack text check (stack in ('Rails', '.NET')),
    triage_note text not null default '',
    triaged_by uuid references public.profiles (id) on delete set null,
    gitlab_project text,
    gitlab_iid integer,
    gitlab_url text,
    gitlab_state text,
    merge_requests jsonb not null default '[]',
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now()
);
create index issue_reports_org_id_idx on public.issue_reports (org_id, created_at desc);
create index issue_reports_reported_by_idx on public.issue_reports (reported_by);
create index issue_reports_triaged_by_idx on public.issue_reports (triaged_by);

-- Filing sets who filed it and starts it at New; any change stamps updated_at (and who triaged it, when that's not the reporter).
create function private.stamp_issue_report() returns trigger
language plpgsql security definer set search_path = ''
as $$
begin
    if tg_op = 'INSERT' then
        new.reported_by := (select auth.uid());
        new.reporter_name := coalesce((select p.full_name from public.profiles p where p.id = new.reported_by), '');
        new.status := 'New';
        new.priority := null; new.stack := null; new.triage_note := ''; new.triaged_by := null;
        new.gitlab_project := null; new.gitlab_iid := null; new.gitlab_url := null; new.gitlab_state := null; new.merge_requests := '[]';
    else
        new.triaged_by := (select auth.uid());
    end if;
    new.updated_at := now();
    return new;
end;
$$;
revoke all on function private.stamp_issue_report() from public, anon, authenticated;
create trigger stamp_issue_report before insert or update on public.issue_reports for each row execute function private.stamp_issue_report();

alter table public.issue_reports enable row level security;
revoke all on public.issue_reports from anon, authenticated;
grant select on public.issue_reports to authenticated;
grant insert (org_id, title, kind, description, context) on public.issue_reports to authenticated;
grant update (title, kind, description, status, priority, stack, triage_note, gitlab_project, gitlab_iid, gitlab_url, gitlab_state, merge_requests)
    on public.issue_reports to authenticated;

create policy "Members and their account manager read requests" on public.issue_reports for select to authenticated
    using (private.is_member(org_id) or private.can_triage(org_id));
create policy "Members file requests" on public.issue_reports for insert to authenticated
    with check (private.is_member(org_id));
create policy "Account managers triage requests" on public.issue_reports for update to authenticated
    using (private.can_triage(org_id)) with check (private.can_triage(org_id));

-- Every request an account manager can triage, with its company name (they aren't members of the companies they manage).
create function public.list_intake()
returns table (id uuid, org_id uuid, org_name text, reported_by uuid, reporter_name text, title text, kind text, description text, context text,
               status text, priority text, stack text, triage_note text, gitlab_project text, gitlab_iid integer, gitlab_url text,
               gitlab_state text, merge_requests jsonb, created_at timestamptz, updated_at timestamptz)
language sql stable security definer set search_path = ''
as $$
    select r.id, r.org_id, o.name, r.reported_by, r.reporter_name, r.title, r.kind, r.description, r.context,
           r.status, r.priority, r.stack, r.triage_note, r.gitlab_project, r.gitlab_iid, r.gitlab_url,
           r.gitlab_state, r.merge_requests, r.created_at, r.updated_at
    from public.issue_reports r join public.organizations o on o.id = r.org_id
    where private.can_triage(r.org_id)
    order by case r.status when 'New' then 0 when 'Triaged' then 1 else 2 end, r.created_at desc;
$$;

revoke all on function public.my_memberships(), public.list_companies(), public.save_company(uuid, text, boolean, uuid),
    public.list_staff(), public.list_intake() from public, anon;
grant execute on function public.my_memberships(), public.list_companies(), public.save_company(uuid, text, boolean, uuid),
    public.list_staff(), public.list_intake() to authenticated;

-- Requests go into the company's activity log too; GitLab bookkeeping fields are left out of the details line. --------

create or replace function private.log_change() returns trigger
language plpgsql security definer set search_path = ''
as $$
declare
    actor uuid := (select auth.uid());
    entity text := case tg_table_name when 'clients' then 'Client' when 'jobs' then 'Job' when 'projects' then 'Project' when 'tasks' then 'Task'
                                      when 'issue_reports' then 'Request' else 'Report' end;
    now_row jsonb := to_jsonb(new);
    was_row jsonb := case when tg_op = 'UPDATE' then to_jsonb(old) end;
    subject text := coalesce(now_row ->> 'title', now_row ->> 'name', '');
    changes text[] := '{}';
    field text;
    action text;
begin
    if actor is null then return new; end if;
    if tg_op = 'INSERT' then
        action := case when tg_table_name = 'issue_reports' then 'Filed request' else 'Created ' || lower(entity) end;
    else
        for field in select jsonb_object_keys(now_row) loop
            continue when field in ('id', 'org_id', 'created_at', 'updated_at', 'sort_order', 'closed_at', 'reported_by', 'reporter_name', 'triaged_by',
                                    'gitlab_project', 'gitlab_iid', 'gitlab_state', 'merge_requests');
            continue when (now_row -> field) is not distinct from (was_row -> field);
            changes := changes || case when field in ('description', 'summary', 'triage_note')
                then private.field_label(field) || ' edited'
                when field = 'gitlab_url' then 'Sent to GitLab: ' || coalesce(now_row ->> field, '—')
                else private.field_label(field) || ': ' || private.field_value(field, was_row ->> field) || ' → ' || private.field_value(field, now_row ->> field) end;
        end loop;
        if cardinality(changes) = 0 then return new; end if;
        action := case when tg_table_name = 'tasks' and cardinality(changes) = 1 and changes[1] like 'Status:%' then 'Moved task'
                       when tg_table_name = 'issue_reports' and was_row ->> 'gitlab_url' is null and now_row ->> 'gitlab_url' is not null then 'Sent request to GitLab'
                       when tg_table_name = 'issue_reports' then 'Updated request'
                       else 'Updated ' || lower(entity) end;
    end if;
    perform private.write_activity(new.org_id, actor, action, entity, subject, array_to_string(changes, '; '));
    return new;
end;
$$;

create trigger log_issue_report_changes after insert or update on public.issue_reports for each row execute function private.log_change();
