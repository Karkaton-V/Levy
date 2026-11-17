# Valuation Workbench (starter)

A small "IDE for assessors": edit valuation rules, press Run, and see the effect on a parcel map, a ratio study and tax bills.

## Open and run in Rider

1. Open `ValuationWorkbench.sln` in JetBrains Rider.
2. Pick the `Workbench.Desktop` run configuration and press Run.
3. On the Editor tab, change `LandRate.N-04 = 9.00` to `11.50` and press F5. Then look at the Map and Compare tabs.

Needs the .NET 8 SDK or newer on Windows (the desktop app is WPF). The first build downloads the Mapsui and AvalonEdit packages from NuGet. The map needs an internet connection for its tiles, and the first launch downloads the study area's streets and buildings from OpenStreetMap (one request, usually under a minute). They are saved in `%LOCALAPPDATA%\ValuationWorkbench\buildings`; delete that folder to download them again.

## Using it

- **Editor tab:** the rules are code. The editor has line numbers, syntax colours and live problem squiggles. Hover a line to see what its rule does or what is wrong with it, Ctrl+Space to pick a rule name, Ctrl+F to find, F5 to run. Click a problem in the list to jump to its line.
- **Map tab:** hover a parcel to preview it, click to open its record, or type a parcel ID (or its last digits) in the search box. In the record, the ⓘ button beside each step of "Why this value" opens the Editor lines behind it and how they combine. Switch the sidebar to **Rules** for a plain-words, read-only summary of what the code does.

## What is in here

| Project | What it does |
|---|---|
| `src/Workbench.Engine` | Rules parser, cost-approach valuation with a step-by-step trace, ratio study (median, COD, PRD), sample parcels. No UI code. |
| `src/Workbench.Desktop` | WPF app with three tabs: Editor, Map, Compare. See the layout below. |
| `tests/Workbench.Engine.Tests` | xUnit tests for the engine. They read the same `DefaultRules.rules` asset the app starts with. |

### Desktop app layout

Code and assets are kept apart, and both use the same sub-folders: one per tab, plus `App` for the window and `Shared` for anything more than one tab uses. No colours, styles, starting rules or map data are written in code.

```
Workbench.Desktop/
  Code/
    App/       App.xaml, MainWindow.xaml (header and tab bar only), MainViewModel.cs (state and Run)
    Editor/    EditorTabView, RulesEditorView (AvalonEdit), MainViewModel.Editor.cs
    Map/       MapTabView, ParcelMapView, ParcelInspectorView, CodeSnippetView, ParcelMap.cs,
               BuildingSource.cs, MapStyle.cs, StudyArea.cs, LouisianaOutline.cs, MainViewModel.Map.cs
    Compare/   CompareTabView, MainViewModel.Compare.cs
    Shared/    Shared.cs (helpers and asset loading), StatTileView
    Settings/  ThemeManager.cs (light/dark switching), AppSettings.cs (saved preferences)
  Assets/
    App/       AppStyles.xaml (navigation rail, top bar, page area)
    Account/   supabase.json (Supabase project URL and publishable key)
    Editor/    EditorStyles.xaml, DefaultRules.rules (the rules the app starts with)
    Map/       MapStyles.xaml, map-style.json (parcel and border colours), study-area.json (border streets, boxes, sample size), louisiana.geojson
    Compare/   CompareStyles.xaml
    Shared/    Theme.xaml (fonts and shared control styles), Rules.Light.xshd / Rules.Dark.xshd (syntax colours)
```

Each asset folder also has `Colors.Light.xaml` and `Colors.Dark.xaml` with the same keys; styles look colours up at runtime, so the theme switches without restarting, and the choice is saved per user.

`MainViewModel` is one class split across files: each tab's folder holds the part that tab uses. The `.xaml` styles are compiled into the app; the data files are copied next to it and read at startup, so you can change a colour, the study area or the starting rules without touching code. The map legend is built from `map-style.json`, so it always matches what the map draws. Changing `study-area.json` downloads that area's streets and buildings again.

## What is placeholder

- **Parcels and sales are made up** (`SampleData.cs`), built so neighborhood N-04 starts out under-assessed.
- **One study area, not the whole city.** The map covers the area around Forest Park Cemetery in Shreveport, bordered by Linwood Avenue, Kings Highway, Fairfield Avenue, and Avery and Parker Streets (set in `Assets/Map/study-area.json`). The border is traced from those streets' real OpenStreetMap geometry (`BuildingSource.TraceBorder`), with a straight join where Avery Street is cut by I-49. If tracing fails, the area falls back to a box.
- **Parcels are building footprints, not lot lines.** Every building inside the border becomes an empty parcel. Real parcel boundaries come from the parish assessor or GIS office.
- **The map stops at Louisiana.** Panning and zooming are limited to the state, and everything outside a simplified state outline (from the PublicaMundi MappingAPI `us-states.json`, saved as `Assets/Map/louisiana.geojson`) is greyed out.
- **The 96 valued parcels sit on real houses but their values are made up.** Their living area is the footprint's area; lot size, age and sales are invented.
- **Millage of 120 is a sample rate.** The 10% assessment ratio and $7,500 homestead exemption follow Louisiana's residential rules.

## Ideas for next steps

- Load real parcel shapes and sales in place of `SampleData.cs`.
- Add PRB and the newer vertical-equity measures to `RatioStudy.cs`.
- Scale to 100k+ parcels, then profile and speed up `Valuer.ValueAll`.
