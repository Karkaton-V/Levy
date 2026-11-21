-- Supabase grants new functions to anon by default; only the sign-up code check should be callable signed out.
revoke execute on function public.my_memberships(), public.team_members(uuid), public.update_member(uuid, uuid, text, text, text),
    public.add_member(uuid, text, text, text), public.list_companies(), public.save_company(uuid, text, boolean) from anon;
