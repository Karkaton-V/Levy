-- Activity log: who did what in a company, and when. Written only by triggers, so the app can't skip or forge an entry; read by the company's admins.

create table public.activity (
    id uuid primary key default gen_random_uuid(),
    org_id uuid not null references public.organizations (id) on delete cascade,
    actor_id uuid references public.profiles (id) on delete set null,
    actor_name text not null default '',
    action text not null,
    entity text not null,
    subject text not null default '',
    details text not null default '',
    created_at timestamptz not null default now()
);
create index activity_org_id_created_at_idx on public.activity (org_id, created_at desc);
create index activity_actor_id_idx on public.activity (actor_id);

alter table public.activity enable row level security;
revoke all on public.activity from anon, authenticated;
grant select on public.activity to authenticated;
create policy "Admins read their company's activity" on public.activity for select to authenticated
    using (private.is_admin(org_id));

-- Readable field names and values for the details line: "Status: To do → In review", "Assignee: Unassigned → Ada Lovelace".

create function private.field_label(field text) returns text
language sql immutable set search_path = ''
as $$ select initcap(left(replace(regexp_replace(field, '_id$', ''), '_', ' '), 1)) || substr(replace(regexp_replace(field, '_id$', ''), '_', ' '), 2); $$;

create function private.field_value(field text, value text) returns text
language plpgsql stable security definer set search_path = ''
as $$
declare
    shown text;
begin
    if value is null or value = '' then return '—'; end if;
    shown := case field
        when 'assignee_id' then (select p.full_name from public.profiles p where p.id = value::uuid)
        when 'owner_id' then (select p.full_name from public.profiles p where p.id = value::uuid)
        when 'author_id' then (select p.full_name from public.profiles p where p.id = value::uuid)
        when 'client_id' then (select c.name from public.clients c where c.id = value::uuid)
        when 'project_id' then (select pr.name from public.projects pr where pr.id = value::uuid)
        when 'job_id' then (select j.title from public.jobs j where j.id = value::uuid)
        else value
    end;
    shown := coalesce(shown, '(removed)');
    return case when length(shown) > 60 then left(shown, 57) || '…' else shown end;
end;
$$;

create function private.write_activity(org uuid, actor uuid, action text, entity text, subject text, details text) returns void
language sql security definer set search_path = ''
as $$
    insert into public.activity (org_id, actor_id, actor_name, action, entity, subject, details)
    values (org, actor, coalesce((select p.full_name from public.profiles p where p.id = actor), ''), action, entity, coalesce(subject, ''), coalesce(details, ''));
$$;

-- Clients, jobs, projects, tasks and reports: one entry per add or real change. Work done without a signed-in user (sample data at sign-up) isn't logged.
create function private.log_change() returns trigger
language plpgsql security definer set search_path = ''
as $$
declare
    actor uuid := (select auth.uid());
    entity text := case tg_table_name when 'clients' then 'Client' when 'jobs' then 'Job' when 'projects' then 'Project' when 'tasks' then 'Task' else 'Report' end;
    now_row jsonb := to_jsonb(new);
    was_row jsonb := case when tg_op = 'UPDATE' then to_jsonb(old) end;
    subject text := coalesce(now_row ->> 'title', now_row ->> 'name', '');
    changes text[] := '{}';
    field text;
    action text;
begin
    if actor is null then return new; end if;
    if tg_op = 'INSERT' then
        action := 'Created ' || lower(entity);
    else
        for field in select jsonb_object_keys(now_row) loop
            continue when field in ('id', 'org_id', 'created_at', 'sort_order', 'closed_at');
            continue when (now_row -> field) is not distinct from (was_row -> field);
            changes := changes || case when field in ('description', 'summary')
                then private.field_label(field) || ' edited'
                else private.field_label(field) || ': ' || private.field_value(field, was_row ->> field) || ' → ' || private.field_value(field, now_row ->> field) end;
        end loop;
        if cardinality(changes) = 0 then return new; end if;
        action := case when tg_table_name = 'tasks' and cardinality(changes) = 1 and changes[1] like 'Status:%' then 'Moved task'
                       else 'Updated ' || lower(entity) end;
    end if;
    perform private.write_activity(new.org_id, actor, action, entity, subject, array_to_string(changes, '; '));
    return new;
end;
$$;

-- People: asking to join, approval, declines, access, role and job title changes.
create function private.log_membership() returns trigger
language plpgsql security definer set search_path = ''
as $$
declare
    actor uuid := coalesce((select auth.uid()), new.user_id);
    person text := coalesce((select p.full_name from public.profiles p where p.id = new.user_id), '');
    action text;
    changes text[] := '{}';
begin
    if tg_op = 'INSERT' then
        action := case when new.status = 'pending' then 'Asked to join'
                       when actor = new.user_id then 'Started the company'
                       else 'Added person' end;
        changes := array['Role: ' || initcap(new.role)] || case when new.job_title <> '' then array['Job title: ' || new.job_title] else '{}' end;
    else
        if new.status is distinct from old.status then
            action := case when new.status = 'active' and old.status = 'pending' then 'Approved person'
                           when new.status = 'active' then 'Restored access'
                           when new.status = 'rejected' then 'Declined person'
                           when new.status = 'disabled' then 'Turned off access'
                           else 'Changed access' end;
            changes := changes || ('Access: ' || initcap(old.status) || ' → ' || initcap(new.status));
        end if;
        if new.role is distinct from old.role then
            action := coalesce(action, case when new.role = 'admin' then 'Made admin' else 'Made member' end);
            changes := changes || ('Role: ' || initcap(old.role) || ' → ' || initcap(new.role));
        end if;
        if new.job_title is distinct from old.job_title then
            action := coalesce(action, 'Set job title');
            changes := changes || ('Job title: ' || coalesce(nullif(old.job_title, ''), '—') || ' → ' || coalesce(nullif(new.job_title, ''), '—'));
        end if;
        if action is null then return new; end if;
    end if;
    perform private.write_activity(new.org_id, actor, action, 'Person', person, array_to_string(changes, '; '));
    return new;
end;
$$;

revoke all on function private.field_label(text), private.field_value(text, text), private.write_activity(uuid, uuid, text, text, text, text),
    private.log_change(), private.log_membership() from public, anon, authenticated;

create trigger log_client_changes after insert or update on public.clients for each row execute function private.log_change();
create trigger log_job_changes after insert or update on public.jobs for each row execute function private.log_change();
create trigger log_project_changes after insert or update on public.projects for each row execute function private.log_change();
create trigger log_task_changes after insert or update on public.tasks for each row execute function private.log_change();
create trigger log_report_changes after insert or update on public.reports for each row execute function private.log_change();
create trigger log_membership_changes after insert or update on public.organization_members for each row execute function private.log_membership();
