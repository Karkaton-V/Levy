using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Workbench.Desktop;

// One legend entry: a swatch drawn by the template in MapTabView.xaml.
public sealed record LegendEntry(string Label, Brush Fill, Brush? Outline, Thickness OutlineThickness, double Width, double Height);

// The Map tab. Its view-model side is MainViewModel.Map.cs.
public partial class MapTabView : UserControl
{
    public MapTabView()
    {
        InitializeComponent();
        Legend.ItemsSource = LegendFor(MapStyle.Default);
    }

    // The legend reads the same looks the map draws with, so the two can't drift apart.
    static IReadOnlyList<LegendEntry> LegendFor(MapStyle style)
    {
        var entries = new List<LegendEntry>();
        foreach (var category in Enum.GetValues<RatioCategory>())
            entries.Add(Swatch(style.Look(category).Label ?? category.ToString(), style.Look(category).Fill, null, 0));
        entries.Add(Swatch(style.Empty.Label ?? "Empty parcel", style.Empty.Fill, style.Empty.Outline, 1));
        entries.Add(new LegendEntry(style.Border.Label ?? "Study area border", ToBrush(style.Border.Line), null, new Thickness(0), 22, 5));
        return entries;
    }

    static LegendEntry Swatch(string label, string? fill, string? outline, double outlineWidth) =>
        new(label, ToBrush(fill), outline is null ? null : ToBrush(outline), new Thickness(outline is null ? 0 : outlineWidth), 14, 14);

    static Brush ToBrush(string? hex)
    {
        if (hex is null) return Brushes.Transparent;
        var (a, r, g, b) = MapStyle.Argb(hex);
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }
}
