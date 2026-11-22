---
layout: page
title: Accounts and security
permalink: /accounts/
---

## Signing in
- Accounts are Supabase Auth users. The app talks to Supabase's Auth and REST APIs with a plain `HttpClient` (`Code/Account/SupabaseClient.cs`).
- The refresh token is kept in `%LOCALAPPDATA%\ValuationWorkbench\session.bin`, encrypted with Windows DPAPI for the current user, so the next launch signs straight in.
- **Sign in with GitLab** uses OAuth with PKCE through the browser and a listener on `localhost:53682`.
- The settings menu has **Change password**, **Sign out** and **Sign out of all devices**.
- `Assets/Account/supabase.json` holds the project URL and a publishable key. That key is safe to ship in a desktop app; row-level security is what protects the data.

## Companies, codes and admin review
- Every workflow row belongs to one company (`org_id`).
- **No code at sign-up:** a database trigger creates your office, makes you its admin and fills it with sample data.
- **With a code:** each company has a join code. The app checks it first (this works signed out), then signs you up as a pending member. Pending, declined and disabled members see nothing until an admin approves them on **Team**.
- **Platform admins** (`profiles.is_platform_admin`) run the Companies page. The flag is set only in the database.
- **Account managers** are platform staff chosen per company; only they (or a platform admin) can triage that company's requests.

## Row-level security
- **Isolation:** row-level security is on for every table. A policy lets you read and write a row only if `private.is_member(org_id)` says you're an active member of its company.
- **Checked functions:** admin actions (approving, job titles, roles, adding people, badges, deleting accounts, companies, account managers) go through security-definer functions that check who's calling.
- **Self-promotion blocked:** column-level grants stop you changing your own role or making yourself a platform admin. On your own profile you can change only your name, bio, pronouns, colours and pictures.
- **No deletes:** the app has no delete permission on any table. The one exception is `delete_account`, which an admin can use on someone who works only under them; it's logged first.
- **Requests:** filing stamps who and when, and always starts at New.
- **Activity log:** written only by triggers, readable only by the company's admins, writable by nobody.
- **Tests:** pgTAP tests in `supabase/tests/database` check all of this in CI (see [CI/CD](ci-cd.md)).

## Profiles
Clicking your name in the side panel opens a Discord-style profile card: banner, picture, name, pronouns, job title with the company tag, up to five badges, and the bio (cut to three lines, with View Full Bio).

**Edit profile** (in the settings menu) opens over the main window with a live preview:
- name, bio, and pronouns (none, he/him, she/her or they/them);
- a picture and a banner image, each with a cropper (drag to move, drag a corner or scroll to resize);
- the profile colour and the code colours (below).

Pictures are scaled and stored in the private `profile-media` Storage bucket. You can write only your own folder, and only colleagues in your company can read it.

## Badges
Company admins award badges on **Team**, each with a reason, and can take them back. The catalogue is `Assets/Settings/badges.json`. Awards are stored in `badge_awards` and written to the activity log.

## Company tags
A small tag next to people's names on their cards, like a Discord server tag. A company admin picks one of nine icons (bank, briefcase, building, chart, shield, globe, map, star, flag) and up to four capital letters or digits on **Team**. The database checks both, and either both are set or neither is. The icons are listed in `Assets/Settings/company-tags.json`.

## Profile colour and code colours
- **Profile colour** fills the banner when there's no banner image. It also becomes the app's accent (buttons, links, the selected side-panel item, focus rings) and tints the backgrounds. `ThemeManager` shifts the blue-tinted theme colours to its hue; blue leaves them as they are.
- **Code colours** pick the rules editor's scheme, separately from the profile colour: Match the app (follows light or dark), Midnight, Ember, Forest or Paper. They're saved in `profiles.code_theme`, and the editor shows a live sample.
- **Light and dark themes** switch instantly from the settings menu, along with **Reduce motion**.
- The last profile colour and code colours are kept locally, so the app opens in them before the profile loads.
