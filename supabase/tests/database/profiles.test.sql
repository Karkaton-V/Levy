-- Profiles, badges and account deletion, run by `supabase test db` (pgTAP) against a fresh local database.
-- Ann runs an office; Cal joins it; Ben runs another office; Eve joined Ann's office and also asked to join Ben's.
begin;
create extension if not exists pgtap with schema extensions;
select plan(20);

insert into auth.users (id, instance_id, email, raw_user_meta_data, aud, role) values
    ('a0000000-0000-0000-0000-000000000001', '00000000-0000-0000-0000-000000000000', 'ann@example.com', '{"full_name":"Ann Admin"}', 'authenticated', 'authenticated'),
    ('b0000000-0000-0000-0000-000000000002', '00000000-0000-0000-0000-000000000000', 'ben@example.com', '{"full_name":"Ben Other"}', 'authenticated', 'authenticated');
select set_config('test.ann_org', (select org_id::text from public.organization_members where user_id = 'a0000000-0000-0000-0000-000000000001'), false);
select set_config('test.ben_org', (select org_id::text from public.organization_members where user_id = 'b0000000-0000-0000-0000-000000000002'), false);

insert into auth.users (id, instance_id, email, raw_user_meta_data, aud, role)
select v.id::uuid, '00000000-0000-0000-0000-000000000000', v.email, jsonb_build_object('full_name', v.name, 'company_code', o.join_code), 'authenticated', 'authenticated'
from (values ('c0000000-0000-0000-0000-000000000003', 'cal@example.com', 'Cal Joiner'), ('e0000000-0000-0000-0000-000000000005', 'eve@example.com', 'Eve Both')) v(id, email, name)
cross join public.organizations o where o.id = current_setting('test.ann_org')::uuid;
update public.organization_members set status = 'active' where org_id = current_setting('test.ann_org')::uuid;
insert into public.organization_members (org_id, user_id, role, status) values (current_setting('test.ben_org')::uuid, 'e0000000-0000-0000-0000-000000000005', 'member', 'pending');

-- Cal edits his own profile, and only his own ---------------------------------------------------------------------

set local role authenticated;
select set_config('request.jwt.claims', '{"sub":"c0000000-0000-0000-0000-000000000003","role":"authenticated"}', true);

select is((with changed as (update public.profiles set bio = 'Residential appraiser.', pronouns = 'he/him', banner_color = 'teal' where id = 'c0000000-0000-0000-0000-000000000003' returning 1) select count(*) from changed),
    1::bigint, 'people edit their own bio, pronouns and banner');
select is_empty($$ update public.profiles set bio = 'hacked' where id = 'a0000000-0000-0000-0000-000000000001' returning 1 $$, 'nobody edits someone else''s profile');
select throws_ok($$ update public.profiles set avatar_path = 'a0000000-0000-0000-0000-000000000001/x.png' where id = 'c0000000-0000-0000-0000-000000000003' $$,
    '23514', null, 'a profile picture must sit in its owner''s folder');
select throws_ok($$ select * from public.award_badge(current_setting('test.ann_org')::uuid, 'a0000000-0000-0000-0000-000000000001', 'mentor', '') $$,
    '42501', null, 'members can''t award badges');
select throws_ok($$ insert into public.badge_awards (org_id, user_id, badge) values (current_setting('test.ann_org')::uuid, 'c0000000-0000-0000-0000-000000000003', 'mentor') $$,
    '42501', null, 'nobody writes badges directly');
select throws_ok($$ select public.delete_account(current_setting('test.ann_org')::uuid, 'e0000000-0000-0000-0000-000000000005') $$,
    '42501', null, 'members can''t delete accounts');
select ok(private.can_see_media('a0000000-0000-0000-0000-000000000001'), 'colleagues see each other''s pictures');
select ok(not private.can_see_media('b0000000-0000-0000-0000-000000000002'), 'people outside the company don''t');
select ok(not private.can_see_media('not-an-id'), 'only real user folders count');

-- Ann awards and takes back badges, and deletes accounts that work only under her ----------------------------------------

reset role;
set local role authenticated;
select set_config('request.jwt.claims', '{"sub":"a0000000-0000-0000-0000-000000000001","role":"authenticated"}', true);

select is((select badge from public.award_badge(current_setting('test.ann_org')::uuid, 'c0000000-0000-0000-0000-000000000003', 'mentor', 'Trained two new appraisers')),
    'mentor', 'an admin awards a badge');
select throws_ok($$ select * from public.award_badge(current_setting('test.ann_org')::uuid, 'a0000000-0000-0000-0000-000000000001', 'mentor', '') $$,
    'P0001', 'You can''t award a badge to yourself.', 'nobody awards themselves a badge');
select ok(exists (select 1 from public.activity where action = 'Awarded badge' and subject = 'Cal Joiner'), 'awards are logged');
select throws_ok($$ select public.delete_account(current_setting('test.ann_org')::uuid, 'a0000000-0000-0000-0000-000000000001') $$,
    'P0001', 'You can''t delete your own account here.', 'admins can''t delete themselves');
select throws_ok($$ select public.delete_account(current_setting('test.ann_org')::uuid, 'e0000000-0000-0000-0000-000000000005') $$,
    'P0001', 'They also work for another company, so only a platform admin can delete their account.', 'people who also work elsewhere are left alone');

-- Ben, another company, sees none of it --------------------------------------------------------------------------

reset role;
set local role authenticated;
select set_config('request.jwt.claims', '{"sub":"b0000000-0000-0000-0000-000000000002","role":"authenticated"}', true);
select is((select count(*) from public.badge_awards), 0::bigint, 'other companies don''t see the badges');

-- Cal sees his badge; then Ann takes it back and deletes his account ------------------------------------------------

reset role;
set local role authenticated;
select set_config('request.jwt.claims', '{"sub":"c0000000-0000-0000-0000-000000000003","role":"authenticated"}', true);
select is((select count(*) from public.badge_awards where user_id = 'c0000000-0000-0000-0000-000000000003'), 1::bigint, 'people see their own badges');

reset role;
set local role authenticated;
select set_config('request.jwt.claims', '{"sub":"a0000000-0000-0000-0000-000000000001","role":"authenticated"}', true);
select public.revoke_badge((select id from public.badge_awards where user_id = 'c0000000-0000-0000-0000-000000000003'));
select is((select count(*) from public.badge_awards), 0::bigint, 'an admin takes a badge back');
select public.delete_account(current_setting('test.ann_org')::uuid, 'c0000000-0000-0000-0000-000000000003');
select ok(exists (select 1 from public.activity where action = 'Deleted account' and subject = 'Cal Joiner' and actor_name = 'Ann Admin'), 'deletions are logged');
select is((select count(*) from public.clients), 8::bigint, 'the company''s work stays after an account is deleted');

reset role;
select is((select count(*) from auth.users where id = 'c0000000-0000-0000-0000-000000000003'), 0::bigint, 'the account is gone');

select * from finish();
rollback;
