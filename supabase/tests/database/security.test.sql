-- Security rules of the workspace schema, run by `supabase test db` (pgTAP) against a fresh local database with every migration applied.
-- Three people: Ann and Ben each sign up and get an office of their own; Cal signs up with Ann's company code and waits for review.
begin;
create extension if not exists pgtap with schema extensions;
select plan(24);

-- People -------------------------------------------------------------------------------------------------------------

insert into auth.users (id, instance_id, email, raw_user_meta_data, aud, role) values
    ('a0000000-0000-0000-0000-000000000001', '00000000-0000-0000-0000-000000000000', 'ann@example.com', '{"full_name":"Ann Admin"}', 'authenticated', 'authenticated'),
    ('b0000000-0000-0000-0000-000000000002', '00000000-0000-0000-0000-000000000000', 'ben@example.com', '{"full_name":"Ben Other"}', 'authenticated', 'authenticated');

insert into auth.users (id, instance_id, email, raw_user_meta_data, aud, role)
select 'c0000000-0000-0000-0000-000000000003', '00000000-0000-0000-0000-000000000000', 'cal@example.com',
       jsonb_build_object('full_name', 'Cal Joiner', 'company_code', lower(o.join_code)), 'authenticated', 'authenticated'
from public.organizations o join public.organization_members m on m.org_id = o.id
where m.user_id = 'a0000000-0000-0000-0000-000000000001';

-- Ann's company and its code, kept in session settings so every role below can use them.
select set_config('test.ann_org', (select org_id::text from public.organization_members where user_id = 'a0000000-0000-0000-0000-000000000001'), false);
select set_config('test.ann_code', (select o.join_code from public.organizations o where o.id = current_setting('test.ann_org')::uuid), false);

select is((select count(*) from public.clients where org_id = current_setting('test.ann_org')::uuid), 8::bigint, 'sign-up seeds an office with sample clients');
select is((select status from public.organization_members where user_id = 'c0000000-0000-0000-0000-000000000003'), 'pending', 'joining with a company code waits for review');
select is((select count(*) from public.organization_members where user_id = 'c0000000-0000-0000-0000-000000000003'), 1::bigint, 'joining with a code starts no office of its own');

-- Ben sees his own company only -------------------------------------------------------------------------------------

set local role authenticated;
select set_config('request.jwt.claims', '{"sub":"b0000000-0000-0000-0000-000000000002","role":"authenticated"}', true);

select is((select count(*) from public.clients), 8::bigint, 'a member sees their own company''s clients');
select is((select count(*) from public.clients where org_id = current_setting('test.ann_org')::uuid), 0::bigint, 'a member sees nothing from another company');
select throws_ok($$ select * from public.team_members(current_setting('test.ann_org')::uuid) $$, '42501', null, 'only that company''s admins can list its people');
select throws_ok($$ delete from public.clients $$, '42501', null, 'rows are never deleted from the app');

-- Cal, still pending, sees nothing ----------------------------------------------------------------------------------

reset role;
set local role authenticated;
select set_config('request.jwt.claims', '{"sub":"c0000000-0000-0000-0000-000000000003","role":"authenticated"}', true);

select is((select count(*) from public.clients), 0::bigint, 'a pending person sees no company data');
select is((select status from public.my_memberships()), 'pending', 'a pending person can see that they are waiting');
select throws_ok($$ update public.profiles set is_platform_admin = true where id = 'c0000000-0000-0000-0000-000000000003' $$, '42501', null,
    'nobody can make themselves a platform admin');

-- Ann, the admin, approves Cal ------------------------------------------------------------------------------------

reset role;
set local role authenticated;
select set_config('request.jwt.claims', '{"sub":"a0000000-0000-0000-0000-000000000001","role":"authenticated"}', true);

select is((select count(*) from public.team_members(current_setting('test.ann_org')::uuid)), 2::bigint, 'an admin sees everyone who joined or asked to');
select is((select status from public.update_member(current_setting('test.ann_org')::uuid, 'c0000000-0000-0000-0000-000000000003', 'member', 'active', 'Field appraiser')),
    'active', 'an admin approves a request to join');
select throws_ok($$ select * from public.update_member(current_setting('test.ann_org')::uuid, 'a0000000-0000-0000-0000-000000000001', 'member', 'active', '') $$,
    'P0001', 'The company needs at least one active admin. Make someone else an admin first.', 'a company keeps at least one admin');
select throws_ok($$ select * from public.list_companies() $$, '42501', null, 'only platform admins see every company');

-- Cal, now a member, files a request and can't triage it -------------------------------------------------------------

reset role;
set local role authenticated;
select set_config('request.jwt.claims', '{"sub":"c0000000-0000-0000-0000-000000000003","role":"authenticated"}', true);

select is((select count(*) from public.clients), 8::bigint, 'an approved member sees the company''s data');
insert into public.issue_reports (org_id, title, kind, description, context) values (current_setting('test.ann_org')::uuid, 'Map freezes on zoom', 'Bug', 'Steps', 'Parcel map');
select is((select status || ' / ' || reporter_name from public.issue_reports), 'New / Cal Joiner', 'a filed request starts New, under the reporter''s name');
select is_empty($$ update public.issue_reports set status = 'Fixed' returning 1 $$, 'members can''t change a request after filing it');
select is((select count(*) from public.activity), 0::bigint, 'members can''t read the activity log');
select throws_ok($$ insert into public.activity (org_id, action, entity) values (current_setting('test.ann_org')::uuid, 'Forged', 'Job') $$, '42501', null,
    'nobody can write to the activity log directly');
select is((select count(*) from public.list_intake()), 0::bigint, 'members don''t see the intake queue');

-- Ann reads the log; signed-out callers get the code check only ----------------------------------------------------

reset role;
set local role authenticated;
select set_config('request.jwt.claims', '{"sub":"a0000000-0000-0000-0000-000000000001","role":"authenticated"}', true);

select ok(exists (select 1 from public.activity where action = 'Approved person' and actor_name = 'Ann Admin'), 'the log records who approved whom');
select ok(exists (select 1 from public.activity where action = 'Filed request' and actor_name = 'Cal Joiner'), 'the log records filed requests');

reset role;
set local role anon;
select set_config('request.jwt.claims', '{"role":"anon"}', true);

select is(public.company_for_code(current_setting('test.ann_code')), 'Ann Admin''s office',
    'the sign-up form can check a company code while signed out');
select throws_ok($$ select * from public.my_memberships() $$, '42501', null, 'signed-out callers can''t use the account functions');

reset role;
select * from finish();
rollback;
