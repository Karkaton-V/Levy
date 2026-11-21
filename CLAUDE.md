# Levy (Valuation Workbench)

C#/.NET 8 WPF desktop app for mass appraisal, built as a portfolio piece for tax-assessor software roles. Solution: `ValuationWorkbench.sln`; app assembly `ValuationWorkbench`, namespace `Workbench.Desktop`.

Work in progress is described in `docs/HANDOFF.md`. Read it before starting.

## Projects
- `src/Workbench.Engine`: valuation rules language, valuer, IAAO ratio study (median, COD, PRD). Plain .NET 8, no UI.
- `src/Workbench.Desktop`: WPF app (AvalonEdit for the rules editor, Mapsui for the parcel map).
- `tests/Workbench.Engine.Tests`: xUnit tests for the engine.

## Layout rules
- `Code/` and `Assets/` use the same sub-folders: `App`, `Shared`, then one folder per page (`Editor`, `Map`, `Compare`, ...).
- No colours, styles, starting data or other assets in `Code/`. Styles are `.xaml` in `Assets/<folder>/`; data files (`.json`, `.geojson`, `.xshd`, `.rules`) are copied to the output and read through `Shared.AssetPath` / `Shared.ReadAsset`.
- Colours live only in `Assets/<folder>/Colors.Light.xaml` and `Colors.Dark.xaml` (same keys in both). Reference them with `{DynamicResource Key}`, never StaticResource, so `ThemeManager` can switch themes at runtime. Code that copies a colour (AvalonEdit) re-reads it on `ThemeManager.Changed`.
- Styles and fonts use `{StaticResource}`. Each style dictionary merges `Assets/Shared/Theme.xaml` itself.
- `MainViewModel` is one class split into partial files, one per page folder (`MainViewModel.Editor.cs`, `.Map.cs`, `.Compare.cs`).
- Functions used by several files go in `Code/Shared/Shared.cs` rather than many small utility files.
- Comments are a single line where possible.

## Building and checking
- The WPF app only builds on Windows (the owner runs it from JetBrains Rider). On Linux, only the engine and the non-WPF desktop code can be compiled.
- Engine tests: `dotnet test tests/Workbench.Engine.Tests`.

## Supabase
- Project ref `hrzalbzhskbjninplrlu`, URL and publishable key in `src/Workbench.Desktop/Assets/Account/supabase.json` (publishable keys are safe in a desktop client; row-level security protects the data).
- Schema changes go in `supabase/migrations/` and reach the hosted project through CI on merge to `main` (see `docs/HANDOFF.md`).
