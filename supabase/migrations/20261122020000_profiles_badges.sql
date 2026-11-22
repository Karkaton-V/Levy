-- Profiles, badges and account deletion: people describe themselves (bio, pronouns, picture, banner); upper management awards
-- badges; a company admin can delete the account of someone who works only under them.

-- Profile fields. Pictures live in the private profile-media bucket under the owner's id: "<user id>/<file>". ------------

alter table public.profiles
    add column bio text not null default '' check (length(bio) <= 300),
    add column pronouns text not null default '' check (length(pronouns) <= 30),
    add column banner_color text not null default 'blue' check (banner_color in ('blue', 'teal', 'green', 'amber', 'rose', 'purple', 'slate')),
    add column avatar_path text,
    add column banner_path text,
    add constraint profiles_media_paths_check check (
        (avatar_path is null or avatar_path like id::text || '/%') and (banner_path is null or banner_path like id::text || '/%'));

revoke update on public.profiles from authenticated;
grant update (full_name, bio, pronouns, banner_color, avatar_path, banner_path) on public.profiles to authenticated;

-- Pictures: private; people manage their own folder and see the pictures of people they work with.
insert into storage.buckets (id, name, public) values ('profile-media', 'profile-media', false) on conflict (id) do nothing;

-- Whether a folder name is the id of someone the caller works with (or the caller); false for anything that isn't an id.
create function private.can_see_media(folder text) returns boolean
language sql stable security definer set search_path = ''
as $$
    select folder ~ '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
       and (folder::uuid = (select auth.uid()) or private.shares_org(folder::uuid));
$$;
revoke all on function private.can_see_media(text) from public, anon;
grant execute on function private.can_see_media(text) to authenticated;

create policy "People see colleagues' profile pictures" on storage.objects for select to authenticated
    using (bucket_id = 'profile-media' and private.can_see_media((storage.foldername(name))[1]));
create policy "People upload their own profile pictures" on storage.objects for insert to authenticated
    with check (bucket_id = 'profile-media' and (storage.foldername(name))[1] = (select auth.uid())::text);
create policy "People replace their own profile pictures" on storage.objects for update to authenticated
    using (bucket_id = 'profile-media' and (storage.foldername(name))[1] = (select auth.uid())::text)
    with check (bucket_id = 'profile-media' and (storage.foldername(name))[1] = (select auth.uid())::text);
create policy "People remove their own profile pictures" on storage.objects for delete to authenticated
    using (bucket_id = 'profile-media' and (storage.foldername(name))[1] = (select auth.uid())::text);

-- Badges: awarded by a company's admins (or platform admins) to its active members, with a reason. -----------------------
-- The badge itself is a key into the catalogue the app ships (Assets/Settings/badges.json).

create table public.badge_awards (
    id uuid primary key default gen_random_uuid(),
    org_id uuid not null references public.organizations (id) on delete cascade,
    user_id uuid not null references public.profiles (id) on delete cascade,
    badge text not null check (badge ~ '^[a-z][a-z-]{1,40}$'),
    reason text not null default '' check (length(reason) <= 200),
    awarded_by uuid references public.profiles (id) on delete set null,
    awarded_by_name text not null default '',
    created_at timestamptz not null default now()
);
create index badge_awards_org_id_idx on public.badge_awards (org_id);
create index badge_awards_user_id_idx on public.badge_awards (user_id);
create index badge_awards_awarded_by_idx on public.badge_awards (awarded_by);

alter table public.badge_awards enable row level security;
revoke all on public.badge_awards from anon, authenticated;
grant select on public.badge_awards to authenticated;
create policy "People see their own badges and their company's" on public.badge_awards for select to authenticated
    using (user_id = (select auth.uid()) or private.is_member(org_id));

-- Awards a badge to an active member; never to yourself.
create function public.award_badge(org uuid, member uuid, badge_key text, why text)
returns setof public.badge_awards
language plpgsql security definer set search_path = ''
as $$
declare
    award public.badge_awards;
    person text;
begin
    if not (private.is_admin(org) or private.is_platform_admin()) then
        raise exception 'Only an admin of this company can award badges.' using errcode = '42501';
    end if;
    if member = (select auth.uid()) then raise exception 'You can''t award a badge to yourself.'; end if;
    if not exists (select 1 from public.organization_members m where m.org_id = org and m.user_id = member and m.status = 'active') then
        raise exception 'Badges go to active members of this company.';
    end if;
    insert into public.badge_awards (org_id, user_id, badge, reason, awarded_by, awarded_by_name)
    values (org, member, badge_key, trim(coalesce(why, '')), (select auth.uid()),
            coalesce((select p.full_name from public.profiles p where p.id = (select auth.uid())), ''))
    returning * into award;
    person := coalesce((select p.full_name from public.profiles p where p.id = member), '');
    perform private.write_activity(org, (select auth.uid()), 'Awarded badge', 'Person', person,
        'Badge: ' || badge_key || case when award.reason <> '' then '; Reason: ' || award.reason else '' end);
    return next award;
end;
$$;

create function public.revoke_badge(award uuid) returns void
language plpgsql security definer set search_path = ''
as $$
declare
    gone public.badge_awards;
begin
    select * into gone from public.badge_awards a where a.id = award;
    if not found then raise exception 'That badge was already taken back.'; end if;
    if not (private.is_admin(gone.org_id) or private.is_platform_admin()) then
        raise exception 'Only an admin of this company can take back badges.' using errcode = '42501';
    end if;
    delete from public.badge_awards a where a.id = award;
    perform private.write_activity(gone.org_id, (select auth.uid()), 'Took back badge', 'Person',
        coalesce((select p.full_name from public.profiles p where p.id = gone.user_id), ''), 'Badge: ' || gone.badge);
end;
$$;

-- Deleting an account: an admin may delete a member (not another admin, not a platform admin, not themselves) whose every
-- membership is in companies the caller administers. Their work stays, unassigned; the deletion is logged first.
create function public.delete_account(org uuid, member uuid) returns void
language plpgsql security definer set search_path = ''
as $$
declare
    who public.profiles;
begin
    if not (private.is_admin(org) or private.is_platform_admin()) then
        raise exception 'Only an admin of this company can delete accounts.' using errcode = '42501';
    end if;
    if member = (select auth.uid()) then raise exception 'You can''t delete your own account here.'; end if;
    select * into who from public.profiles p where p.id = member;
    if not found then raise exception 'That account no longer exists.'; end if;
    if who.is_platform_admin then raise exception 'Platform admin accounts can''t be deleted here.'; end if;
    if not exists (select 1 from public.organization_members m where m.org_id = org and m.user_id = member) then
        raise exception 'That person is not part of this company.';
    end if;
    if exists (select 1 from public.organization_members m where m.org_id = org and m.user_id = member and m.role = 'admin') then
        raise exception 'Make them a member first: admins can''t delete other admins.';
    end if;
    if not private.is_platform_admin() and exists (
        select 1 from public.organization_members m where m.user_id = member and not private.is_admin(m.org_id)) then
        raise exception 'They also work for another company, so only a platform admin can delete their account.';
    end if;
    perform private.write_activity(org, (select auth.uid()), 'Deleted account', 'Person', who.full_name, 'Email: ' || who.email);
    delete from auth.users u where u.id = member;
end;
$$;

revoke all on function public.award_badge(uuid, uuid, text, text), public.revoke_badge(uuid), public.delete_account(uuid, uuid) from public, anon;
grant execute on function public.award_badge(uuid, uuid, text, text), public.revoke_badge(uuid), public.delete_account(uuid, uuid) to authenticated;
