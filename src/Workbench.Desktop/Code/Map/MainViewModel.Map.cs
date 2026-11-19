using System.Windows.Input;
using Mapsui.Projections;
using Workbench.Engine;
using Nts = NetTopologySuite.Geometries;
using static Workbench.Desktop.Shared;

namespace Workbench.Desktop;

// One valued parcel: the building it sits on (-1 before footprints load), how its ratio compares with the target, and its valuation.
public sealed record ParcelCell(int Index, int BuildingIndex, RatioCategory Category, Valuation Valuation);

// One row of the "why this value" trace; the info drop-down shows Code (the rule lines from the Editor) and Formula.
public sealed record TraceRow(string Rule, string Detail, string Amount, string Formula, string Code, string CodeCaption, int FirstLine)
{
    public bool HasCode => Code.Length > 0;
}

// The Map tab's part of the view model: buildings, the selected parcel's record, search, hover text and the plain-words rules.
public sealed partial class MainViewModel
{
    int[] _parcelBuilding = Array.Empty<int>();
    Dictionary<int, int> _buildingParcel = new();
    ParcelCell? _selectedCell;
    int? _selectedEmpty;
    bool _sidebarShowsRules;
    bool _isMapExpanded;

    // True while the full-size map fills the page under the top bar; false shows the simplified overview.
    public bool IsMapExpanded
    {
        get => _isMapExpanded;
        set { if (_isMapExpanded == value) return; _isMapExpanded = value; Raise(); }
    }

    public ICommand ToggleMapSizeCommand => _toggleMapSize ??= new RelayCommand(() => IsMapExpanded = !IsMapExpanded);
    ICommand? _toggleMapSize;

    // Raised when the user asks to zoom the map to the selected parcel.
    public event Action? ZoomToSelectionRequested;

    public ICommand ZoomToSelectedCommand { get; }
    public IReadOnlyList<Building> Buildings { get; private set; } = Array.Empty<Building>();
    public Nts.Polygon? Border { get; private set; }
    public IReadOnlyList<ParcelCell> Cells { get; private set; } = Array.Empty<ParcelCell>();
    public string StudyAreaDescription => _area.Description;
    public string StudyAreaName => _area.Name;
    public string MapMessage { get; private set; } = "Loading the study area";
    public bool HasMapMessage => MapMessage.Length > 0;
    public string ZoomHint => $"Zoom in to see the {Number(Buildings.Count - _parcelBuilding.Length, "N0")} empty parcels";
    public string RulesInWords { get; private set; } = "";
    public bool SidebarShowsRules { get => _sidebarShowsRules; set => SetSidebar(value); }
    public bool SidebarShowsRecord { get => !_sidebarShowsRules; set => SetSidebar(!value); }
    public IReadOnlyList<TraceRow> Trace { get; private set; } = Array.Empty<TraceRow>();
    public string SelectedTitle { get; private set; } = "No parcel selected";
    public string SelectedSubtitle { get; private set; } = "Click a parcel on the map to see how its value was worked out.";
    public string SelectedMarket { get; private set; } = "";
    public string SelectedSale { get; private set; } = "";
    public bool HasSelection => SelectedBuildingIndex is not null;

    // The building to outline on the map: the selected parcel's, or the selected empty one.
    public int? SelectedBuildingIndex => _selectedCell is { BuildingIndex: >= 0 } cell ? cell.BuildingIndex : _selectedEmpty;

    public ParcelCell? SelectedCell
    {
        get => _selectedCell;
        set { _selectedCell = value; if (value is not null) _selectedEmpty = null; RefreshSelection(); Raise(); Raise(nameof(SelectedBuildingIndex)); Raise(nameof(HasSelection)); }
    }

    // Selects what stands on a building: its valued parcel, or the empty parcel the building represents.
    public void SelectBuilding(int buildingIndex)
    {
        if (_buildingParcel.TryGetValue(buildingIndex, out var parcelIndex) && parcelIndex < Cells.Count) { SelectedCell = Cells[parcelIndex]; return; }
        _selectedCell = null;
        _selectedEmpty = buildingIndex;
        RefreshSelection();
        Raise(nameof(SelectedCell)); Raise(nameof(SelectedBuildingIndex)); Raise(nameof(HasSelection));
    }

    // Finds a valued parcel by its ID, or by the end of its ID when only one parcel matches; returns false if none does.
    public bool SelectParcelById(string text)
    {
        var id = text.Trim();
        if (id.Length == 0) return false;
        var cell = Cells.FirstOrDefault(c => c.Valuation.Parcel.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (cell is null)
        {
            var endings = Cells.Where(c => c.Valuation.Parcel.Id.EndsWith(id, StringComparison.OrdinalIgnoreCase)).Take(2).ToList();
            if (endings.Count == 1) cell = endings[0];
        }
        if (cell is null) return false;
        SelectedCell = cell;
        return true;
    }

    // One-line description shown when the mouse is over a building on the map.
    public string DescribeBuilding(int buildingIndex)
    {
        if (_buildingParcel.TryGetValue(buildingIndex, out var parcel) && parcel < Cells.Count)
        {
            var cell = Cells[parcel];
            return $"Parcel {cell.Valuation.Parcel.Id} · {Money(cell.Valuation.MarketValue)} · {MapStyle.Default.Word(cell.Category)}";
        }
        return buildingIndex < Buildings.Count ? $"Empty parcel · about {Number(Buildings[buildingIndex].AreaSqFt, "N0")} sq ft" : "";
    }

    // Downloads (or reads saved) the study area, then moves the sample parcels onto real houses inside it.
    public async Task LoadBuildingsAsync()
    {
        var done = false;
        var progress = new Progress<string>(message => { if (done) return; MapMessage = message; Raise(nameof(MapMessage)); Raise(nameof(HasMapMessage)); });
        string finalMessage;
        try
        {
            var load = await BuildingSource.LoadAsync(_area, progress);
            ApplyBuildings(load.Buildings, load.Border);
            finalMessage = load.Problem ?? (load.Buildings.Count == 0 ? "No buildings were found inside the study area." : "");
        }
        catch (Exception e) { finalMessage = "Couldn't load building footprints: " + e.Message; }
        done = true;
        MapMessage = finalMessage;
        Raise(nameof(MapMessage)); Raise(nameof(HasMapMessage));
    }

    // Puts the sample parcels on the house-sized buildings nearest the middle of the area, using each footprint as the living area.
    public void ApplyBuildings(IReadOnlyList<Building> buildings, Nts.Polygon? border = null)
    {
        var (sampleLon, sampleLat) = border is not null ? SphericalMercator.ToLonLat(border.Centroid.X, border.Centroid.Y)
            : buildings.Count > 0 ? (buildings.Average(b => b.Lon), buildings.Average(b => b.Lat)) : (0, 0);
        var cos = Math.Cos(sampleLat * Math.PI / 180);
        double Distance(Building b) => Math.Pow((b.Lon - sampleLon) * cos, 2) + Math.Pow(b.Lat - sampleLat, 2);
        var sample = _area.Sample;
        var picked = Enumerable.Range(0, buildings.Count)
            .Where(i => buildings[i].AreaSqFt >= sample.MinHouseSqFt && buildings[i].AreaSqFt <= sample.MaxHouseSqFt)
            .OrderBy(i => Distance(buildings[i])).Take(sample.Count)
            .OrderBy(i => buildings[i].Lon)
            .ToArray();
        Buildings = buildings;
        Border = border;
        _parcelBuilding = picked;
        _buildingParcel = picked.Select((building, parcel) => (building, parcel)).ToDictionary(x => x.building, x => x.parcel);
        _parcels = picked.Length > 0 ? SampleData.Create(picked.Select(i => (int)Math.Round(buildings[i].AreaSqFt)).ToList()) : SampleData.Create(count: sample.Count);
        _runNumber = 0;
        _previous = null;
        _selectedCell = null;
        _selectedEmpty = null;
        Run();
    }

    // After a run: pair each valuation with its building and ratio category.
    void UpdateMapAfterRun() => Cells = _current.Select(v =>
        new ParcelCell(v.Parcel.Index, v.Parcel.Index < _parcelBuilding.Length ? _parcelBuilding[v.Parcel.Index] : -1, CategoryOf(v.Ratio), v)).ToList();

    // Which parcel to reselect after a run: the same one, the first one, or none when an empty parcel is selected.
    int RememberSelection() => _selectedEmpty is null ? _selectedCell?.Index ?? 0 : -1;

    void RestoreSelection(int parcelIndex)
    {
        if (parcelIndex >= 0) SelectedCell = parcelIndex < Cells.Count ? Cells[parcelIndex] : null;
        else RefreshSelection();
    }

    void SetSidebar(bool showRules)
    {
        if (_sidebarShowsRules == showRules) return;
        _sidebarShowsRules = showRules;
        Raise(nameof(SidebarShowsRules)); Raise(nameof(SidebarShowsRecord));
    }

    // The sidebar's plain-words version of the rules as they are typed now.
    void RefreshRulesInWords()
    {
        RulesInWords = RuleSet.Parse(_rulesText, out var problems).ToPlainWords(problems);
        Raise(nameof(RulesInWords));
    }

    // Fills the property record for the selected parcel or empty parcel.
    void RefreshSelection()
    {
        var v = _selectedCell?.Valuation;
        if (v is null && _selectedEmpty is int b && b < Buildings.Count)
        {
            SelectedTitle = "Empty parcel";
            SelectedSubtitle = $"OpenStreetMap building {Buildings[b].OsmId} · about {Number(Buildings[b].AreaSqFt, "N0")} sq ft footprint";
            SelectedMarket = "Not valued";
            SelectedSale = "This building has no record on the roll yet. The coloured parcels carry sample values.";
            Trace = Array.Empty<TraceRow>();
        }
        else if (v is null)
        {
            SelectedTitle = "No parcel selected";
            SelectedSubtitle = "Click a parcel on the map to see how its value was worked out.";
            SelectedMarket = SelectedSale = "";
            Trace = Array.Empty<TraceRow>();
        }
        else
        {
            SelectedTitle = $"Parcel {v.Parcel.Id}";
            SelectedSubtitle = $"{v.Parcel.Neighborhood} · built {v.Parcel.YearBuilt} · {Number(v.Parcel.LivingSqFt, "N0")} sq ft on a {Number(v.Parcel.LotSqFt, "N0")} sq ft lot";
            SelectedMarket = Money(v.MarketValue);
            SelectedSale = v.Ratio is { } ratio ? $"Last sale {Money(v.Parcel.SalePrice!.Value)} · ratio {Number(ratio, "F3")}" : "No recent sale on record";
            Trace = BuildTrace(v);
        }
        Raise(nameof(SelectedTitle)); Raise(nameof(SelectedSubtitle)); Raise(nameof(SelectedMarket)); Raise(nameof(SelectedSale)); Raise(nameof(Trace));
    }

    // Rebuilds the trace so its code snippets match the rules text as it is now.
    void RefreshTrace()
    {
        if (_selectedCell?.Valuation is not { } v) return;
        Trace = BuildTrace(v);
        Raise(nameof(Trace));
    }

    // Each step with the Editor lines that define it; a rule that isn't in the text shows as a comment saying its default is used.
    IReadOnlyList<TraceRow> BuildTrace(Valuation v)
    {
        var rows = _rulesText.Replace("\r\n", "\n").Split('\n');
        var lines = RuleLines(_rulesText);
        return v.Trace.Select(t =>
        {
            var found = t.Keys.Where(lines.ContainsKey).Select(k => lines[k]).Order().ToList();
            var code = string.Join("\n", found.Select(n => rows[n - 1].Trim())
                .Concat(t.Keys.Where(k => !lines.ContainsKey(k)).Select(k => $"# {k} is not in the rules, so its default is used")));
            var caption = found.Count == 0 ? (t.Keys.Count == 0 ? "No rule lines: this step only combines earlier steps." : "Not set in the Editor.")
                : found.Count == 1 ? $"Editor line {found[0]}"
                : found[^1] - found[0] == found.Count - 1 ? $"Editor lines {found[0]}–{found[^1]}"
                : "Editor lines " + string.Join(", ", found);
            return new TraceRow(t.Rule, t.Detail, Money(t.Amount), t.Formula, code, caption, found.FirstOrDefault());
        }).ToList();
    }
}
