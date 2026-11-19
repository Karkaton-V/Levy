using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Workbench.Desktop;

// Hosts the Mapsui control and its overlays, and keeps the map in step with the view model.
// Small (overview): clicks step from Louisiana to a city to the study area, and zooming out steps back. Full size: free pan and zoom with search and view buttons.
public partial class ParcelMapView : UserControl
{
    // A press and release closer than this, in pixels, counts as a click on the overview.
    const double ClickSlop = 6;

    readonly ParcelMap _parcelMap = new(MapStyle.Default, StudyArea.Default, LouisianaOutline.Default, City.All);
    MainViewModel? _viewModel;
    IReadOnlyList<Building>? _shownBuildings;
    Point? _pressedAt;
    // The overview level to return to when the full-size map closes.
    (MapLevel Level, City? City) _overviewPlace = (MapLevel.State, null);

    public ParcelMapView()
    {
        InitializeComponent();
        MapHost.Map = _parcelMap.Map;
        _parcelMap.BuildingTapped += index => _viewModel?.SelectBuilding(index);
        _parcelMap.ViewChanged += () => Dispatcher.InvokeAsync(UpdateZoomHint);
        _parcelMap.LevelChanged += ShowLevel;
        SearchBox.TextChanged += (_, _) =>
        {
            SearchPlaceholder.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            SearchMessage.Visibility = Visibility.Collapsed;
        };
        DataContextChanged += (_, _) => Attach(DataContext as MainViewModel);
        // The overview frames its level for the map's size, so frame it again whenever that size changes (first layout, window resize, closing the full-size map).
        MapHost.SizeChanged += (_, _) => { if (IsOverview) _parcelMap.GoTo(_parcelMap.Level, _parcelMap.CurrentCity); };
        ShowMode();
    }

    bool IsOverview => _viewModel?.IsMapExpanded != true;

    void Attach(MainViewModel? viewModel)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelChanged;
            _viewModel.ZoomToSelectionRequested -= ZoomToSelection;
        }
        _viewModel = viewModel;
        if (viewModel is null) return;
        viewModel.PropertyChanged += OnViewModelChanged;
        viewModel.ZoomToSelectionRequested += ZoomToSelection;
        Sync();
        ShowMode();
        _parcelMap.GoToState();
    }

    void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsMapExpanded)) { SwitchSize(); return; }
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName is nameof(MainViewModel.Buildings) or nameof(MainViewModel.Cells) or nameof(MainViewModel.SelectedBuildingIndex)) Sync();
    }

    // Rebuilds the empty parcels only when a new set of buildings arrives; recolours and reselects every time.
    void Sync()
    {
        if (_viewModel is null) return;
        if (!ReferenceEquals(_shownBuildings, _viewModel.Buildings))
        {
            var firstBuildings = (_shownBuildings?.Count ?? 0) == 0 && _viewModel.Buildings.Count > 0;
            _shownBuildings = _viewModel.Buildings;
            _parcelMap.SetBuildings(_viewModel.Buildings, _viewModel.Cells);
            _parcelMap.SetBorder(_viewModel.Border);
            _parcelMap.ShowValued(_viewModel.Cells);
            if (firstBuildings && _parcelMap.Level == MapLevel.Area) _parcelMap.ZoomToStudyArea();
        }
        else _parcelMap.ShowValued(_viewModel.Cells);
        _parcelMap.Select(_viewModel.SelectedBuildingIndex);
        UpdateZoomHint();
    }

    // Opening keeps the current view; closing goes back to the overview level the map was at before.
    void SwitchSize()
    {
        if (IsOverview) _parcelMap.GoTo(_overviewPlace.Level, _overviewPlace.City);
        else _overviewPlace = (_parcelMap.Level, _parcelMap.CurrentCity);
        ShowMode();
    }

    void ShowMode()
    {
        var overview = IsOverview;
        OverviewControls.Visibility = overview ? Visibility.Visible : Visibility.Collapsed;
        FullControls.Visibility = ZoomButtons.Visibility = overview ? Visibility.Collapsed : Visibility.Visible;
        SearchMessage.Visibility = Visibility.Collapsed;
        ClearHover();
        ShowLevel();
    }

    // The overview's breadcrumb, hint and back button for the current level.
    void ShowLevel()
    {
        var city = _parcelMap.CurrentCity?.Name;
        var area = StudyArea.Default.Name;
        (Breadcrumb.Text, LevelHint.Text, BackButton.Content) = _parcelMap.Level switch
        {
            MapLevel.Area => ($"Louisiana  ›  {city}  ›  {area}", "Click a parcel to open its record.", $"Back to {city}"),
            MapLevel.City when _parcelMap.CurrentCity?.HasStudyArea == true => ($"Louisiana  ›  {city}", $"Click the shaded {area} study area.", "Back to Louisiana"),
            MapLevel.City => ($"Louisiana  ›  {city}", "No study area here yet.", "Back to Louisiana"),
            _ => ("Louisiana", "Click a city to zoom in.", ""),
        };
        BackButton.Visibility = _parcelMap.Level == MapLevel.State ? Visibility.Collapsed : Visibility.Visible;
        UpdateZoomHint();
    }

    // Shows "zoom in to see empty parcels" only when there are some, they are hidden at this zoom level, and parcels are what the map is showing.
    void UpdateZoomHint() =>
        ZoomHint.Visibility = _viewModel is { Buildings.Count: > 0 } && !_parcelMap.EmptyParcelsVisible && (!IsOverview || _parcelMap.Level == MapLevel.Area)
            ? Visibility.Visible : Visibility.Collapsed;

    // Overview input: the map itself never pans or zooms; a click steps in, the wheel steps in or out.
    void Map_PreviewDown(object sender, MouseButtonEventArgs e)
    {
        if (!IsOverview) return;
        _pressedAt = e.GetPosition(MapHost);
        e.Handled = true;
    }

    void Map_PreviewUp(object sender, MouseButtonEventArgs e)
    {
        if (!IsOverview) return;
        var position = e.GetPosition(MapHost);
        if (_pressedAt is Point down && (position - down).Length <= ClickSlop) ClickOverview(position);
        _pressedAt = null;
        e.Handled = true;
    }

    void Map_PreviewWheel(object sender, MouseWheelEventArgs e)
    {
        if (!IsOverview) return;
        if (e.Delta < 0) _parcelMap.LevelUp();
        else if (_parcelMap.Level != MapLevel.Area) ClickOverview(e.GetPosition(MapHost));
        e.Handled = true;
    }

    void ClickOverview(Point at)
    {
        switch (_parcelMap.Level)
        {
            case MapLevel.State when _parcelMap.FindCityAtScreen(at.X, at.Y) is City city:
                ClearHover();
                _parcelMap.GoToCity(city);
                break;
            case MapLevel.City when _parcelMap.CurrentCity?.HasStudyArea == true && _parcelMap.IsStudyAreaAtScreen(at.X, at.Y):
                ClearHover();
                _parcelMap.GoToArea();
                break;
            case MapLevel.Area when _parcelMap.FindBuildingAtScreen(at.X, at.Y) is int building:
                _viewModel?.SelectBuilding(building);
                break;
        }
    }

    // Hover: on the overview, what a click would open at this level; on parcels, a one-line preview of the building.
    void Map_MouseMove(object sender, MouseEventArgs e)
    {
        if (_viewModel is null || e.LeftButton == MouseButtonState.Pressed) { ClearHover(); return; }
        var position = e.GetPosition(MapHost);
        string? text = null;
        int? building = null;
        if (IsOverview && _parcelMap.Level == MapLevel.State)
            text = _parcelMap.FindCityAtScreen(position.X, position.Y) is City city ? $"{city.Name} · click to zoom in" : null;
        else if (IsOverview && _parcelMap.Level == MapLevel.City)
            text = _parcelMap.CurrentCity?.HasStudyArea == true && _parcelMap.IsStudyAreaAtScreen(position.X, position.Y) ? $"{StudyArea.Default.Name} study area · click to see its parcels" : null;
        else
        {
            building = _parcelMap.FindBuildingAtScreen(position.X, position.Y);
            text = building is int i ? _viewModel.DescribeBuilding(i) : null;
            if (text is null && !IsOverview && _parcelMap.FindCityAtScreen(position.X, position.Y) is City city) text = $"{city.Name} · click to zoom in";
        }
        _parcelMap.Hover(building);
        MapHost.Cursor = text is null ? null : Cursors.Hand;
        if (text is null) { HoverLabel.Visibility = Visibility.Collapsed; return; }
        HoverText.Text = text;
        HoverLabel.Visibility = Visibility.Visible;
        HoverLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var left = position.X + 16 + HoverLabel.DesiredSize.Width > ActualWidth ? position.X - 16 - HoverLabel.DesiredSize.Width : position.X + 16;
        Canvas.SetLeft(HoverLabel, Math.Max(0, left));
        Canvas.SetTop(HoverLabel, Math.Max(0, position.Y + 18));
    }

    void Map_MouseLeave(object sender, MouseEventArgs e) => ClearHover();

    void ClearHover()
    {
        _parcelMap.Hover(null);
        MapHost.Cursor = null;
        HoverLabel.Visibility = Visibility.Collapsed;
    }

    // "Zoom to this parcel" from the record: on the overview, open the study area first.
    void ZoomToSelection()
    {
        if (_viewModel?.SelectedBuildingIndex is not int i) return;
        if (IsOverview && _parcelMap.Level != MapLevel.Area) _parcelMap.GoToArea();
        _parcelMap.ZoomToBuilding(i);
    }

    void Find()
    {
        if (_viewModel is null) return;
        if (_viewModel.SelectParcelById(SearchBox.Text)) { ZoomToSelection(); return; }
        SearchMessageText.Text = SearchBox.Text.Trim().Length == 0 ? "Type a parcel ID first, such as 04-2115." : $"No parcel matches \"{SearchBox.Text.Trim()}\".";
        SearchMessage.Visibility = Visibility.Visible;
    }

    void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        Find();
        e.Handled = true;
    }

    void Find_Click(object sender, RoutedEventArgs e) => Find();

    void Back_Click(object sender, RoutedEventArgs e) => _parcelMap.LevelUp();

    void ZoomIn_Click(object sender, RoutedEventArgs e) => _parcelMap.ZoomIn();

    void ZoomOut_Click(object sender, RoutedEventArgs e) => _parcelMap.ZoomOut();

    void Sample_Click(object sender, RoutedEventArgs e) => _parcelMap.ZoomToSample();

    void Area_Click(object sender, RoutedEventArgs e) => _parcelMap.ZoomToStudyArea();

    void Louisiana_Click(object sender, RoutedEventArgs e) => _parcelMap.ZoomToLouisiana();
}
