using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Workbench.Desktop;

// Hosts the Mapsui control and its overlays, and keeps the map in step with the view model.
public partial class ParcelMapView : UserControl
{
    readonly ParcelMap _parcelMap = new(MapStyle.Default, StudyArea.Default, LouisianaOutline.Default);
    MainViewModel? _viewModel;
    IReadOnlyList<Building>? _shownBuildings;

    public ParcelMapView()
    {
        InitializeComponent();
        MapHost.Map = _parcelMap.Map;
        _parcelMap.BuildingTapped += index => _viewModel?.SelectBuilding(index);
        _parcelMap.ViewChanged += () => Dispatcher.InvokeAsync(UpdateZoomHint);
        SearchBox.TextChanged += (_, _) =>
        {
            SearchPlaceholder.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            SearchMessage.Visibility = Visibility.Collapsed;
        };
        DataContextChanged += (_, _) => Attach(DataContext as MainViewModel);
    }

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
        _parcelMap.ZoomToStudyArea();
    }

    void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
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
            if (firstBuildings) _parcelMap.ZoomToStudyArea();
        }
        else _parcelMap.ShowValued(_viewModel.Cells);
        _parcelMap.Select(_viewModel.SelectedBuildingIndex);
        UpdateZoomHint();
    }

    // Shows "zoom in to see empty parcels" only when there are some and they are hidden at this zoom level.
    void UpdateZoomHint() =>
        ZoomHint.Visibility = _viewModel is { Buildings.Count: > 0 } && !_parcelMap.EmptyParcelsVisible ? Visibility.Visible : Visibility.Collapsed;

    // Outlines the building under the mouse and shows a one-line preview beside the cursor.
    void Map_MouseMove(object sender, MouseEventArgs e)
    {
        if (_viewModel is null || e.LeftButton == MouseButtonState.Pressed) { ClearHover(); return; }
        var position = e.GetPosition(MapHost);
        var index = _parcelMap.FindBuildingAtScreen(position.X, position.Y);
        _parcelMap.Hover(index);
        MapHost.Cursor = index is null ? null : Cursors.Hand;
        if (index is not int i) { HoverLabel.Visibility = Visibility.Collapsed; return; }
        HoverText.Text = _viewModel.DescribeBuilding(i);
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

    void ZoomToSelection()
    {
        if (_viewModel?.SelectedBuildingIndex is int i) _parcelMap.ZoomToBuilding(i);
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

    void ZoomIn_Click(object sender, RoutedEventArgs e) => _parcelMap.ZoomIn();

    void ZoomOut_Click(object sender, RoutedEventArgs e) => _parcelMap.ZoomOut();

    void Sample_Click(object sender, RoutedEventArgs e) => _parcelMap.ZoomToSample();

    void Area_Click(object sender, RoutedEventArgs e) => _parcelMap.ZoomToStudyArea();

    void Louisiana_Click(object sender, RoutedEventArgs e) => _parcelMap.ZoomToLouisiana();
}
