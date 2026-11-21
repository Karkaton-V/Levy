-- Company admins and account review: people join a company with its code and wait for an admin to approve them;
-- admins set job titles, roles and access. Platform admins (a flag set by hand in the database) create companies and assign people to them.

-- Roles are admin or member; each membership has a status and a job title. -------------------------------------

alter table public.organization_members drop constraint organization_members_role_check;
update public.organization_members set role = 'admin' where role = 'owner';
alter table public.organization_members
    add constraint organization_members_role_check check (role in ('admin', 'member')),
    add column status text not null default 'active' check (status in ('pending', 'active', 'rejected', 'disabled')),
    add column job_title text not null default '',
    add column decided_at timestamptz,
    add column decided_by uuid references public.profiles (id) on delete set null;
create index organization_members_decided_by_idx on public.organization_members (decided_by);

-- Platform admins can create companies and assign anyone to them. Only set from the database, never from the app.
alter table public.profiles add column is_platform_admin boolean not null default false;

-- The code people type at sign-up to ask to join a company.
create function private.new_join_code() returns text
language sql volatile set search_path = ''
as $$ select upper(substr(replace(gen_random_uuid()::text, '-', ''), 1, 8)); $$;

alter table public.organizations add column join_code text unique default private.new_join_code();
update public.organizations set join_code = private.new_join_code() where join_code is null;
alter table public.organizations alter column join_code set not null;

-- Membership checks: only active members count; admins are active members with the admin role. --------------------

create or replace function private.is_member(org uuid) returns boolean
language sql stable security definer set search_path = ''
as $$
    select exists (select 1 from public.organization_members m
                   where m.org_id = org and m.user_id = (select auth.uid()) and m.status = 'active');
$$;

create function private.is_admin(org uuid) returns boolean
language sql stable security definer set search_path = ''
as $$
    select exists (select 1 from public.organization_members m
                   where m.org_id = org and m.user_id = (select auth.uid()) and m.status = 'active' and m.role = 'admin');
$$;

create function private.is_platform_admin() returns boolean
language sql stable security definer set search_path = ''
as $$ select coalesce((select p.is_platform_admin from public.profiles p where p.id = (select auth.uid())), false); $$;

-- Colleagues: active members see each other; admins also see the people waiting to join.
create or replace function private.shares_org(other uuid) returns boolean
language sql stable security definer set search_path = ''
as $$
    select exists (
        select 1 from public.organization_members mine
        join public.organization_members theirs on theirs.org_id = mine.org_id
        where mine.user_id = (select auth.uid()) and mine.status = 'active' and theirs.user_id = other
          and (theirs.status = 'active' or mine.role = 'admin'));
$$;

revoke all on function private.is_admin(uuid), private.is_platform_admin(), private.new_join_code() from public, anon;
grant execute on function private.is_admin(uuid), private.is_platform_admin() to authenticated;

-- Policies and grants. ---------------------------------------------------------------------------------------------

-- Users may change their own name only; the platform admin flag is not theirs to set.
revoke update on public.profiles from authenticated;
grant update (full_name) on public.profiles to authenticated;

-- Admins rename their company; the join code changes only through the database.
revoke update on public.organizations from authenticated;
grant update (name) on public.organizations to authenticated;
drop policy "Owners rename their organizations" on public.organizations;
create policy "Admins rename their organizations" on public.organizations for update to authenticated
    using (private.is_admin(id)) with check (private.is_admin(id));

-- Members see active colleagues; admins see everyone who asked to join; everyone sees their own memberships.
drop policy "Members see their organizations' members" on public.organization_members;
create policy "Members see their organizations' members" on public.organization_members for select to authenticated
    using (user_id = (select auth.uid()) or private.is_admin(org_id) or (status = 'active' and private.is_member(org_id)));

-- Functions the app calls (POST /rest/v1/rpc/...). Each checks who is asking. -----------------------------------

-- The caller's memberships, active or not, so the app can say "waiting for approval". The join code is shown to admins only.
create function public.my_memberships()
returns table (org_id uuid, org_name text, role text, status text, job_title text, join_code text, is_platform_admin boolean, joined_at timestamptz)
language sql stable security definer set search_path = ''
as $$
    select m.org_id, o.name, m.role, m.status, m.job_title,
           case when m.role = 'admin' and m.status = 'active' then o.join_code end,
           private.is_platform_admin(), m.created_at
    from public.organization_members m join public.organizations o on o.id = m.org_id
    where m.user_id = (select auth.uid())
    order by (m.status = 'active') desc, m.created_at;
$$;

-- The company a code belongs to, checked on the sign-up form before the account is made (callable signed out).
create function public.company_for_code(code text) returns text
language sql stable security definer set search_path = ''
as $$ select o.name from public.organizations o where o.join_code = upper(trim(code)); $$;

-- One company's people, for its admins and for platform admins.
create function public.team_members(org uuid)
returns table (id uuid, org_id uuid, full_name text, email text, role text, status text, job_title text, created_at timestamptz, decided_at timestamptz)
language plpgsql stable security definer set search_path = ''
as $$
begin
    if not (private.is_admin(org) or private.is_platform_admin()) then
        raise exception 'Only an admin of this company can see its people.' using errcode = '42501';
    end if;
    return query
        select p.id, m.org_id, p.full_name, p.email, m.role, m.status, m.job_title, m.created_at, m.decided_at
        from public.organization_members m join public.profiles p on p.id = m.user_id
        where m.org_id = org
        order by case m.status when 'pending' then 0 when 'active' then 1 else 2 end, p.full_name;
end;
$$;

-- Approves, rejects, disables or edits one person. A company always keeps at least one active admin.
create function public.update_member(org uuid, member uuid, new_role text, new_status text, new_job_title text)
returns table (id uuid, org_id uuid, full_name text, email text, role text, status text, job_title text, created_at timestamptz, decided_at timestamptz)
language plpgsql security definer set search_path = ''
as $$
declare
    existing public.organization_members;
begin
    if not (private.is_admin(org) or private.is_platform_admin()) then
        raise exception 'Only an admin of this company can change its people.' using errcode = '42501';
    end if;
    if new_role not in ('admin', 'member') then raise exception 'Unknown role "%".', new_role; end if;
    if new_status not in ('pending', 'active', 'rejected', 'disabled') then raise exception 'Unknown status "%".', new_status; end if;
    select * into existing from public.organization_members m where m.org_id = org and m.user_id = member for update;
    if not found then raise exception 'That person is not part of this company.'; end if;
    if existing.status = 'active' and existing.role = 'admin' and (new_status <> 'active' or new_role <> 'admin')
       and not exists (select 1 from public.organization_members m
                       where m.org_id = org and m.user_id <> member and m.status = 'active' and m.role = 'admin') then
        raise exception 'The company needs at least one active admin. Make someone else an admin first.';
    end if;
    update public.organization_members m
       set role = new_role, status = new_status, job_title = trim(coalesce(new_job_title, '')),
           decided_at = case when m.status <> new_status then now() else m.decided_at end,
           decided_by = case when m.status <> new_status then (select auth.uid()) else m.decided_by end
     where m.org_id = org and m.user_id = member;
    return query select * from public.team_members(org) t where t.id = member;
end;
$$;

-- Adds an existing account to a company straight away (no review), or reactivates them.
create function public.add_member(org uuid, member_email text, new_role text, new_job_title text)
returns table (id uuid, org_id uuid, full_name text, email text, role text, status text, job_title text, created_at timestamptz, decided_at timestamptz)
language plpgsql security definer set search_path = ''
as $$
declare
    person uuid;
begin
    if not (private.is_admin(org) or private.is_platform_admin()) then
        raise exception 'Only an admin of this company can add people to it.' using errcode = '42501';
    end if;
    if new_role not in ('admin', 'member') then raise exception 'Unknown role "%".', new_role; end if;
    select p.id into person from public.profiles p where lower(p.email) = lower(trim(member_email));
    if person is null then raise exception 'No account uses %. Ask them to create one first.', trim(member_email); end if;
    insert into public.organization_members as m (org_id, user_id, role, status, job_title, decided_at, decided_by)
    values (org, person, new_role, 'active', trim(coalesce(new_job_title, '')), now(), (select auth.uid()))
    on conflict on constraint organization_members_pkey do update
        set role = excluded.role, status = 'active', job_title = excluded.job_title, decided_at = now(), decided_by = excluded.decided_by;
    return query select * from public.team_members(org) t where t.id = person;
end;
$$;

-- Every company with its head count, for platform admins.
create function public.list_companies()
returns table (id uuid, name text, join_code text, members integer, pending integer, admins integer, created_at timestamptz)
language plpgsql stable security definer set search_path = ''
as $$
begin
    if not private.is_platform_admin() then raise exception 'Only a platform admin can see every company.' using errcode = '42501'; end if;
    return query
        select o.id, o.name, o.join_code,
               (select count(*)::integer from public.organization_members m where m.org_id = o.id and m.status = 'active'),
               (select count(*)::integer from public.organization_members m where m.org_id = o.id and m.status = 'pending'),
               (select count(*)::integer from public.organization_members m where m.org_id = o.id and m.status = 'active' and m.role = 'admin'),
               o.created_at
        from public.organizations o
        order by o.name;
end;
$$;

-- Creates a company (optionally with sample work in it) or renames one; platform admins only.
create function public.save_company(company uuid, company_name text, with_sample_data boolean)
returns table (id uuid, name text, join_code text, members integer, pending integer, admins integer, created_at timestamptz)
language plpgsql security definer set search_path = ''
as $$
declare
    org uuid := company;
begin
    if not private.is_platform_admin() then raise exception 'Only a platform admin can create or rename companies.' using errcode = '42501'; end if;
    if nullif(trim(company_name), '') is null then raise exception 'Give the company a name.'; end if;
    if org is null then
        insert into public.organizations (name) values (trim(company_name)) returning organizations.id into org;
        if with_sample_data then perform private.seed_workspace(org, (select auth.uid())); end if;
    else
        update public.organizations o set name = trim(company_name) where o.id = org;
        if not found then raise exception 'That company no longer exists.'; end if;
    end if;
    return query select * from public.list_companies() c where c.id = org;
end;
$$;

revoke all on function public.my_memberships(), public.company_for_code(text), public.team_members(uuid),
    public.update_member(uuid, uuid, text, text, text), public.add_member(uuid, text, text, text),
    public.list_companies(), public.save_company(uuid, text, boolean) from public;
grant execute on function public.my_memberships(), public.team_members(uuid), public.update_member(uuid, uuid, text, text, text),
    public.add_member(uuid, text, text, text), public.list_companies(), public.save_company(uuid, text, boolean) to authenticated;
grant execute on function public.company_for_code(text) to anon, authenticated;

-- Sign-up: with a company code, ask to join that company and wait for review; without one, start an office of your own as its admin. --

create or replace function private.handle_new_user() returns trigger
language plpgsql security definer set search_path = ''
as $$
declare
    display_name text := coalesce(nullif(trim(new.raw_user_meta_data ->> 'full_name'), ''), nullif(split_part(coalesce(new.email, ''), '@', 1), ''), 'New user');
    code text := nullif(upper(trim(new.raw_user_meta_data ->> 'company_code')), '');
    org uuid;
begin
    insert into public.profiles (id, full_name, email) values (new.id, display_name, coalesce(new.email, ''));
    if code is not null then
        select o.id into org from public.organizations o where o.join_code = code;
        if org is null then raise exception 'No company uses the code %.', code; end if;
        insert into public.organization_members (org_id, user_id, role, status) values (org, new.id, 'member', 'pending');
    else
        insert into public.organizations (name) values (display_name || '''s office') returning id into org;
        insert into public.organization_members (org_id, user_id, role, status, decided_at) values (org, new.id, 'admin', 'active', now());
        perform private.seed_workspace(org, new.id);
    end if;
    return new;
end;
$$;
