---
layout: page
title: Getting started
permalink: /getting-started/
---

## What you need
- Windows, because the app is WPF.
- The .NET 8 SDK or newer.
- JetBrains Rider or Visual Studio (optional; the command line works too).
- An internet connection: the app signs in to Supabase, and the map loads its tiles and buildings online.

## Build and run

**In Rider or Visual Studio:** open `ValuationWorkbench.sln` and run the `Workbench.Desktop` configuration.

**From the command line:**

```powershell
dotnet build ValuationWorkbench.sln
dotnet run --project src/Workbench.Desktop
```

Engine tests:

```powershell
dotnet test tests/Workbench.Engine.Tests
```

On Linux or macOS only the engine and its tests build; the desktop app needs Windows.

## Sign in

The app opens to **Sign in / Create account** and remembers your session between launches.

- **Create account, no company code:** you get an office of your own, filled with sample clients, jobs, projects, tasks and reports, and you're its admin. This is the quickest way to try everything.
- **Create account with a company code:** you ask to join that company and wait until one of its admins approves you. Until then you see no data.
- **Sign in with GitLab:** for platform staff. While `Assets/Account/gitlab.json` is in demo mode (the default), the button explains that GitLab isn't connected.

## A first run
1. Open **Rules** and change `LandRate.N-04 = 9.00` to `11.50`.
2. Press **F5** (or Run).
3. Open **Parcel map** to see the new values, and click a parcel for its "why this value" trace.
4. Open **Compare runs** to see how the median ratio, COD and PRD moved.

## What's kept on your PC
Everything is under `%LOCALAPPDATA%\ValuationWorkbench`:
- `settings.json`: theme, Reduce motion, and the last profile and code colours, so the app opens in them.
- `session.bin`: the refresh token, encrypted for your Windows account (DPAPI).
- `buildings`: streets and buildings for the study area, downloaded from OpenStreetMap on first launch.
- `mock-gitlab.json`: the demo GitLab's issues and merge requests.
