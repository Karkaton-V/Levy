using Mapsui;
using Mapsui.Extensions;
using Mapsui.Layers;
using Mapsui.Limiting;
using Mapsui.Nts;
using Mapsui.Projections;
using Mapsui.Styles;
using Mapsui.Tiling;
using Mapsui.Widgets;
using Mapsui.Widgets.InfoWidgets;
using Mapsui.Widgets.ScaleBar;
using NetTopologySuite.Index.Strtree;
using MBrush = Mapsui.Styles.Brush;
using MColor = Mapsui.Styles.Color;
using MPen = Mapsui.Styles.Pen;
using Nts = NetTopologySuite.Geometries;

namespace Workbench.Desktop;

// The Mapsui map: an OpenStreetMap basemap limited to Louisiana, the study area's border, one parcel per building, and click hit-testing.
// Colours and widths come from Assets/Map/map-style.json, the outline from Assets/Map/louisiana.geojson.
public sealed class ParcelMap
{
    readonly MapStyle _style;
    readonly StudyArea _area;
    readonly LouisianaOutline _louisiana;
    readonly MemoryLayer _emptyLayer;
    readonly MemoryLayer _valuedLayer = new("ValuedParcels") { Style = null };
    readonly MemoryLayer _hoverLayer = new("Hover") { Style = null };
    readonly MemoryLayer _selectionLayer = new("Selection") { Style = null };
    readonly MemoryLayer _borderLayer = new("StudyAreaBorder") { Style = null };
    readonly VectorStyle _borderCasing, _borderLine, _emptyStyle, _hoverStyle, _selectionStyle;
    readonly Dictionary<RatioCategory, VectorStyle> _valuedStyles;
    IReadOnlyList<Building> _buildings = Array.Empty<Building>();
    HashSet<int> _valued = new();
    STRtree<int> _index = new();
    int? _hovered;
    MRect? _borderBox;

    public ParcelMap(MapStyle style, StudyArea area, LouisianaOutline louisiana)
    {
        _style = style;
        _area = area;
        _louisiana = louisiana;
        _emptyLayer = new MemoryLayer("EmptyParcels") { Style = null, MaxVisible = style.EmptyParcelsMaxResolution };
        _emptyStyle = AreaStyle(style.Empty);
        _hoverStyle = AreaStyle(style.Hover);
        _selectionStyle = AreaStyle(style.Selection);
        _borderCasing = new VectorStyle { Line = new MPen(ToColor(style.Border.Casing), style.Border.CasingWidth) };
        _borderLine = new VectorStyle { Line = new MPen(ToColor(style.Border.Line), style.Border.LineWidth) };
        _valuedStyles = Enum.GetValues<RatioCategory>().ToDictionary(c => c, c => AreaStyle(style.Look(c) with
        {
            Outline = style.ValuedOutline.Outline,
            OutlineWidth = style.ValuedOutline.OutlineWidth,
        }));

        // Mapsui shows log text and an FPS panel while debugging; turn both off.
        LoggingWidget.ShowLoggingInMap = ActiveMode.No;
        Map.Performance.IsActive = ActiveMode.No;

        Map.Layers.Add(OpenStreetMap.CreateTileLayer("ValuationWorkbench/0.1 (student portfolio project)"));
        Map.Layers.Add(CreateLouisianaLayer());
        Map.Layers.Add(_emptyLayer);
        Map.Layers.Add(_valuedLayer);
        Map.Layers.Add(_borderLayer);
        Map.Layers.Add(_hoverLayer);
        Map.Layers.Add(_selectionLayer);
        Map.Widgets.Add(new ScaleBarWidget(Map)
        {
            HorizontalAlignment = Mapsui.Widgets.HorizontalAlignment.Left,
            VerticalAlignment = Mapsui.Widgets.VerticalAlignment.Bottom,
            Margin = new MRect(16),
            MaxWidth = 180,
            UnitConverter = ImperialUnitConverter.Instance,
        });

        Map.Navigator.Limiter = new ViewportLimiterKeepWithinExtent();
        Map.Navigator.OverridePanBounds = ToMapBox(louisiana.West, louisiana.South, louisiana.East, louisiana.North);
        Map.Navigator.OverrideZoomBounds = new MMinMax(0.15, 20000);
        Map.Tapped += OnTapped;
        Map.Navigator.ViewportChanged += (_, _) => ViewChanged?.Invoke();
    }

    public Map Map { get; } = new();

    // Raised with the building's index when the user clicks a parcel.
    public event Action<int>? BuildingTapped;

    // Raised whenever the view pans or zooms.
    public event Action? ViewChanged;

    // True once zoomed in far enough for empty parcels to be drawn.
    public bool EmptyParcelsVisible => Map.Navigator.Viewport.Resolution <= _style.EmptyParcelsMaxResolution;

    // Draws every building as an empty parcel, except those that carry a valued parcel, and indexes all of them for clicks.
    public void SetBuildings(IReadOnlyList<Building> buildings, IReadOnlyList<ParcelCell> cells)
    {
        _buildings = buildings;
        _valued = cells.Where(c => c.BuildingIndex >= 0).Select(c => c.BuildingIndex).ToHashSet();
        var index = new STRtree<int>();
        var features = new List<IFeature>(buildings.Count);
        for (var i = 0; i < buildings.Count; i++)
        {
            index.Insert(buildings[i].Shape.EnvelopeInternal, i);
            if (_valued.Contains(i)) continue;
            var feature = new GeometryFeature(buildings[i].Shape);
            feature.Styles.Add(_emptyStyle);
            features.Add(feature);
        }
        if (buildings.Count > 0) index.Build();
        _index = index;
        _emptyLayer.Features = features;
        _emptyLayer.DataHasChanged();
    }

    // Draws the study area's border as a thick line along its streets, with a white casing so it reads on any basemap.
    public void SetBorder(Nts.Polygon? border)
    {
        var features = new List<IFeature>();
        _borderBox = null;
        if (border is not null)
        {
            var feature = new GeometryFeature(new Nts.LineString(border.ExteriorRing.Coordinates));
            feature.Styles.Add(_borderCasing);
            feature.Styles.Add(_borderLine);
            features.Add(feature);
            var e = border.EnvelopeInternal;
            _borderBox = new MRect(e.MinX, e.MinY, e.MaxX, e.MaxY);
        }
        _borderLayer.Features = features;
        _borderLayer.DataHasChanged();
    }

    // Recolours the valued parcels; they stay visible at every zoom level.
    public void ShowValued(IReadOnlyList<ParcelCell> cells)
    {
        var features = new List<IFeature>(cells.Count);
        foreach (var cell in cells)
        {
            if (cell.BuildingIndex < 0 || cell.BuildingIndex >= _buildings.Count) continue;
            var feature = new GeometryFeature(_buildings[cell.BuildingIndex].Shape);
            feature.Styles.Add(_valuedStyles[cell.Category]);
            features.Add(feature);
        }
        _valuedLayer.Features = features;
        _valuedLayer.DataHasChanged();
    }

    // Outlines the selected building, or clears the outline when nothing is selected.
    public void Select(int? buildingIndex)
    {
        var features = new List<IFeature>();
        if (buildingIndex is int i && i >= 0 && i < _buildings.Count)
        {
            var feature = new GeometryFeature(_buildings[i].Shape);
            feature.Styles.Add(_selectionStyle);
            features.Add(feature);
        }
        _selectionLayer.Features = features;
        _selectionLayer.DataHasChanged();
    }

    // Outlines the building under the mouse; does nothing if it is already the hovered one.
    public void Hover(int? buildingIndex)
    {
        if (_hovered == buildingIndex) return;
        _hovered = buildingIndex;
        var features = new List<IFeature>();
        if (buildingIndex is int i && i >= 0 && i < _buildings.Count)
        {
            var feature = new GeometryFeature(_buildings[i].Shape);
            feature.Styles.Add(_hoverStyle);
            features.Add(feature);
        }
        _hoverLayer.Features = features;
        _hoverLayer.DataHasChanged();
    }

    // Finds the building under a point on screen, in device-independent pixels from the map's top-left corner.
    public int? FindBuildingAtScreen(double screenX, double screenY)
    {
        var viewport = Map.Navigator.Viewport;
        if (viewport.Resolution <= 0) return null;
        var world = viewport.ScreenToWorld(screenX, screenY);
        return FindBuildingAt(world.X, world.Y, viewport.Resolution);
    }

    // Finds the building under a map point; empty parcels only count when they are drawn at this zoom level.
    public int? FindBuildingAt(double x, double y, double resolution)
    {
        if (_buildings.Count == 0) return null;
        var point = new Nts.Point(x, y);
        foreach (var i in _index.Query(new Nts.Envelope(x, x, y, y)))
        {
            if (resolution > _style.EmptyParcelsMaxResolution && !_valued.Contains(i)) continue;
            try { if (_buildings[i].Shape.Contains(point)) return i; }
            catch (Nts.TopologyException) { }
        }
        return null;
    }

    public void ZoomIn() => Map.Navigator.ZoomIn(300);

    public void ZoomOut() => Map.Navigator.ZoomOut(300);

    // Centres on one building at about street level, so its neighbours are visible too.
    public void ZoomToBuilding(int buildingIndex)
    {
        if (buildingIndex < 0 || buildingIndex >= _buildings.Count) return;
        var centre = _buildings[buildingIndex].Shape.EnvelopeInternal.Centre;
        Map.Navigator.CenterOnAndZoomTo(new MPoint(centre.X, centre.Y), 0.6, 400);
    }

    // Frames the valued sample parcels with room around them.
    public void ZoomToSample()
    {
        var boxes = _valued.Select(i => _buildings[i].Shape.EnvelopeInternal).Select(e => new MRect(e.MinX, e.MinY, e.MaxX, e.MaxY)).ToList();
        if (boxes.Count == 0) { ZoomToStudyArea(); return; }
        var all = new MRect(boxes);
        Map.Navigator.ZoomToBox(all.Grow(all.Width * 0.3, all.Height * 0.3), duration: 400);
    }

    // Frames the study area's border, or its box before the border has loaded.
    public void ZoomToStudyArea()
    {
        var b = _area.BuildingBox;
        var box = _borderBox ?? ToMapBox(b.West, b.South, b.East, b.North);
        Map.Navigator.ZoomToBox(box.Grow(box.Width * 0.12, box.Height * 0.12), duration: 400);
    }

    // Zooms out as far as the map allows, which shows all of Louisiana.
    public void ZoomToLouisiana() =>
        Map.Navigator.ZoomToBox(ToMapBox(_louisiana.West, _louisiana.South, _louisiana.East, _louisiana.North), duration: 400);

    // Greys out everything outside Louisiana and outlines the state.
    MemoryLayer CreateLouisianaLayer()
    {
        var points = _louisiana.LonLat;
        var margin = _style.MaskMarginDegrees;
        var ring = new Nts.Coordinate[points.Length / 2];
        for (var i = 0; i < ring.Length; i++)
        {
            var (x, y) = SphericalMercator.FromLonLat(points[2 * i], points[2 * i + 1]);
            ring[i] = new Nts.Coordinate(x, y);
        }
        var outer = ToMapBox(_louisiana.West - margin, _louisiana.South - margin, _louisiana.East + margin, _louisiana.North + margin);
        var shell = new Nts.LinearRing(new[]
        {
            new Nts.Coordinate(outer.MinX, outer.MinY), new Nts.Coordinate(outer.MaxX, outer.MinY), new Nts.Coordinate(outer.MaxX, outer.MaxY),
            new Nts.Coordinate(outer.MinX, outer.MaxY), new Nts.Coordinate(outer.MinX, outer.MinY),
        });
        var look = _style.OutsideLouisiana;
        var mask = new GeometryFeature(new Nts.Polygon(shell, new[] { new Nts.LinearRing(ring) }));
        mask.Styles.Add(new VectorStyle { Fill = new MBrush(ToColor(look.Fill)), Outline = null });
        var outline = new GeometryFeature(new Nts.LineString(ring));
        outline.Styles.Add(new VectorStyle { Line = new MPen(ToColor(look.Outline), look.OutlineWidth) });
        return new MemoryLayer("Louisiana") { Style = null, Features = new List<IFeature> { mask, outline } };
    }

    // A Mapsui fill-and-outline style from one look in map-style.json; a missing colour means none is drawn.
    static VectorStyle AreaStyle(AreaLook look) => new()
    {
        Fill = look.Fill is null ? null : new MBrush(ToColor(look.Fill)),
        Outline = look.Outline is null ? null : new MPen(ToColor(look.Outline), look.OutlineWidth),
    };

    // No colour in the asset means nothing is drawn.
    static MColor ToColor(string? hex)
    {
        if (hex is null) return MColor.Transparent;
        var (a, r, g, b) = MapStyle.Argb(hex);
        return new MColor(r, g, b, a);
    }

    // A lon/lat box as a rectangle in map coordinates.
    static MRect ToMapBox(double west, double south, double east, double north)
    {
        var (minX, minY) = SphericalMercator.FromLonLat(west, south);
        var (maxX, maxY) = SphericalMercator.FromLonLat(east, north);
        return new MRect(minX, minY, maxX, maxY);
    }

    void OnTapped(object? sender, MapEventArgs e)
    {
        if (FindBuildingAt(e.WorldPosition.X, e.WorldPosition.Y, e.Map.Navigator.Viewport.Resolution) is not int i) return;
        BuildingTapped?.Invoke(i);
        e.Handled = true;
    }
}
