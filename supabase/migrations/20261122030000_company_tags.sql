-- Company tags, and the pronoun choices.
-- Company tags: a small badge next to people's names on their profile cards, like a Discord server tag. A company's admins pick
-- one of nine icons (Assets/Settings/company-tags.json) and up to four letters or digits.

alter table public.organizations
    add column tag_icon text check (tag_icon in ('bank', 'briefcase', 'building', 'chart', 'shield', 'globe', 'map', 'star', 'flag')),
    add column tag_text text check (tag_text ~ '^[A-Z0-9]{1,4}$'),
    add constraint organizations_tag_check check ((tag_icon is null) = (tag_text is null));

-- Pronouns are one of three choices, or none.
alter table public.profiles drop constraint profiles_pronouns_check;
alter table public.profiles add constraint profiles_pronouns_check check (pronouns in ('', 'he/him', 'she/her', 'they/them'));

-- Admins already rename their company (policy "Admins rename their organizations"); they may now set its tag too.
revoke update on public.organizations from authenticated;
grant update (name, tag_icon, tag_text) on public.organizations to authenticated;
