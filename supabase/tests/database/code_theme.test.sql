-- Code colours, run by `supabase test db` (pgTAP): people pick their own code theme from the set; nobody can change someone else's.
begin;
create extension if not exists pgtap with schema extensions;
select plan(4);

insert into auth.users (id, instance_id, email, raw_user_meta_data, aud, role) values
    ('a0000000-0000-0000-0000-000000000001', '00000000-0000-0000-0000-000000000000', 'ann@example.com', '{"full_name":"Ann Admin"}', 'authenticated', 'authenticated'),
    ('b0000000-0000-0000-0000-000000000002', '00000000-0000-0000-0000-000000000000', 'ben@example.com', '{"full_name":"Ben Other"}', 'authenticated', 'authenticated');

select is((select code_theme from public.profiles where id = 'a0000000-0000-0000-0000-000000000001'), 'app', 'new profiles follow the app theme');

set local role authenticated;
select set_config('request.jwt.claims', '{"sub":"a0000000-0000-0000-0000-000000000001","role":"authenticated"}', true);
select isnt_empty($$ update public.profiles set code_theme = 'midnight' where id = 'a0000000-0000-0000-0000-000000000001' returning 1 $$, 'people pick a code theme');
select throws_ok($$ update public.profiles set code_theme = 'neon' where id = 'a0000000-0000-0000-0000-000000000001' $$, '23514', null, 'code themes come from the set');
select is_empty($$ update public.profiles set code_theme = 'paper' where id = 'b0000000-0000-0000-0000-000000000002' returning 1 $$, 'nobody changes someone else''s');

reset role;
select * from finish();
rollback;
