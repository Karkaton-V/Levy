-- Company tags, run by `supabase test db` (pgTAP): admins set their company's tag; members and other companies can't.
begin;
create extension if not exists pgtap with schema extensions;
select plan(6);

insert into auth.users (id, instance_id, email, raw_user_meta_data, aud, role) values
    ('a0000000-0000-0000-0000-000000000001', '00000000-0000-0000-0000-000000000000', 'ann@example.com', '{"full_name":"Ann Admin"}', 'authenticated', 'authenticated'),
    ('b0000000-0000-0000-0000-000000000002', '00000000-0000-0000-0000-000000000000', 'ben@example.com', '{"full_name":"Ben Other"}', 'authenticated', 'authenticated');
select set_config('test.ann_org', (select org_id::text from public.organization_members where user_id = 'a0000000-0000-0000-0000-000000000001'), false);
insert into auth.users (id, instance_id, email, raw_user_meta_data, aud, role)
select 'c0000000-0000-0000-0000-000000000003', '00000000-0000-0000-0000-000000000000', 'cal@example.com',
       jsonb_build_object('full_name', 'Cal Joiner', 'company_code', o.join_code), 'authenticated', 'authenticated'
from public.organizations o where o.id = current_setting('test.ann_org')::uuid;
update public.organization_members set status = 'active' where org_id = current_setting('test.ann_org')::uuid;

set local role authenticated;
select set_config('request.jwt.claims', '{"sub":"a0000000-0000-0000-0000-000000000001","role":"authenticated"}', true);
select isnt_empty($$ update public.organizations set tag_icon = 'bank', tag_text = 'CADO' where id = current_setting('test.ann_org')::uuid returning 1 $$,
    'an admin sets the company tag');
select throws_ok($$ update public.organizations set tag_icon = 'bank', tag_text = 'TOOLONG' where id = current_setting('test.ann_org')::uuid $$,
    '23514', null, 'tags are at most four letters or digits');
select throws_ok($$ update public.organizations set tag_icon = 'rocket', tag_text = 'CADO' where id = current_setting('test.ann_org')::uuid $$,
    '23514', null, 'tag icons come from the set of nine');
select throws_ok($$ update public.organizations set join_code = 'ABCDEFGH' where id = current_setting('test.ann_org')::uuid $$,
    '42501', null, 'the join code still can''t be changed from the app');

reset role;
set local role authenticated;
select set_config('request.jwt.claims', '{"sub":"c0000000-0000-0000-0000-000000000003","role":"authenticated"}', true);
select is_empty($$ update public.organizations set tag_text = 'HACK' where id = current_setting('test.ann_org')::uuid returning 1 $$, 'members can''t change the tag');
select is((select tag_icon || ' ' || tag_text from public.organizations where id = current_setting('test.ann_org')::uuid), 'bank CADO', 'members see the tag');

reset role;
select * from finish();
rollback;
