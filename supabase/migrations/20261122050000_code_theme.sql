-- Code colours: each person picks how the rules editor looks, independent of their profile colour (banner_color, which also tints the app).
-- 'app' follows the app's light or dark theme; the others are fixed schemes (Assets/Editor/code-themes.json).

alter table public.profiles
    add column code_theme text not null default 'app' check (code_theme in ('app', 'midnight', 'ember', 'forest', 'paper'));

grant update (code_theme) on public.profiles to authenticated;
