using System.Windows;
using System.Windows.Media;

namespace Workbench.Desktop;

// Loads Assets/<folder>/Colors.Light.xaml or Colors.Dark.xaml into the app's resources and swaps them on demand.
// Styles look colours up with DynamicResource, so everything open recolours at once; code that copies colours listens to Changed.
// On top of the theme, the signed-in person's profile colour becomes the accent and tints the backgrounds, and their code theme colours the editor.
public static class ThemeManager
{
    static readonly string[] Folders = ["Shared", "App", "Editor", "Map", "Compare", "Settings"];
    static readonly List<ResourceDictionary> Loaded = [];
    static readonly ResourceDictionary Tint = [];

    // The keys designed around the blue accent: backgrounds, lines and text with a slight blue tint, and the accent family itself.
    static readonly string[] Tinted =
    [
        "Ground", "Panel", "Line", "TableHead", "Text", "Soft", "Muted", "AccentHover", "AccentSoft", "Link",
        "HoverFill", "HoverFillSoft", "ControlBorder", "InputFill", "SegmentTrack", "SegmentChecked", "ScrollThumb", "ScrollThumbHover",
        "ToneBlueFill", "ToneBlueText",
        "NavBackground", "NavText", "NavMuted", "NavHover", "NavSelected", "NavAccent", "NavLine", "BrandWave",
        "MapOverlay", "MapOverlaySoft", "MapControlFill", "MapControlFillHover", "MapControlBorder", "MapControlFocus", "MapControlText",
        "MapControlPlaceholder", "AfterBar",
    ];

    public static AppTheme Current { get; private set; } = AppTheme.Light;
    public static bool IsDark => Current == AppTheme.Dark;

    // A profile colour (profiles.banner_color: blue, teal, green, amber, rose, purple or slate) and a code theme key (Assets/Editor/code-themes.json).
    public static string ProfileColor { get; private set; } = "blue";
    public static string CodeTheme { get; private set; } = "app";

    public static event Action? Changed;

    // Applies the saved theme, profile colour and code theme; call once at startup, before any window opens.
    public static void Initialize()
    {
        var settings = AppSettings.Load();
        ProfileColor = settings.ProfileColor;
        CodeTheme = settings.CodeTheme;
        Apply(settings.Theme, save: false);
    }

    public static void Apply(AppTheme theme, bool save = true)
    {
        var merged = Application.Current.Resources.MergedDictionaries;
        foreach (var dictionary in Loaded) merged.Remove(dictionary);
        merged.Remove(Tint);
        Loaded.Clear();
        foreach (var folder in Folders)
        {
            var colors = new ResourceDictionary { Source = new Uri($"/ValuationWorkbench;component/Assets/{folder}/Colors.{theme}.xaml", UriKind.Relative) };
            merged.Insert(Loaded.Count, colors);
            Loaded.Add(colors);
        }
        Current = theme;
        FillTint();
        merged.Insert(Loaded.Count, Tint); // After the theme's colours, so its keys win.
        if (save) (AppSettings.Load() with { Theme = theme }).Save();
        Changed?.Invoke();
    }

    // Called when the profile loads or is saved; remembered so the next start opens in the same colours.
    public static void SetProfile(string profileColor, string codeTheme)
    {
        if (profileColor == ProfileColor && codeTheme == CodeTheme) return;
        ProfileColor = profileColor;
        CodeTheme = codeTheme;
        (AppSettings.Load() with { ProfileColor = profileColor, CodeTheme = codeTheme }).Save();
        Apply(Current, save: false);
    }

    // Turns each blue-tinted colour to the profile colour's hue, keeping its lightness; saturation scales with how vivid the profile colour is.
    static void FillTint()
    {
        Tint.Clear();
        var name = char.ToUpperInvariant(ProfileColor[0]) + ProfileColor[1..];
        if (ProfileColor == "blue" || Find("Profile" + name) is not Color seed || Find("Accent") is not SolidColorBrush accent) return;
        var (seedHue, seedSaturation, _) = ToHsl(seed);
        var (_, blueSaturation, _) = ToHsl(accent.Color);
        var scale = blueSaturation > 0 ? seedSaturation / blueSaturation : 0;
        foreach (var key in Tinted)
            if (Find(key) is SolidColorBrush brush)
            {
                var (_, saturation, lightness) = ToHsl(brush.Color);
                Tint[key] = Frozen(FromHsl(seedHue, Math.Clamp(saturation * scale, 0, 1), lightness, brush.Color.A));
            }
        Tint["Accent"] = Frozen(seed);
        if (Find("ProfileOn" + name) is Color onAccent) Tint["OnAccent"] = Frozen(onAccent);
    }

    static object? Find(string key) => Application.Current.TryFindResource(key);

    static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    static (double Hue, double Saturation, double Lightness) ToHsl(Color color)
    {
        double r = color.R / 255.0, g = color.G / 255.0, b = color.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        var lightness = (max + min) / 2;
        if (max == min) return (0, 0, lightness);
        var delta = max - min;
        var saturation = lightness > 0.5 ? delta / (2 - max - min) : delta / (max + min);
        var hue = max == r ? (g - b) / delta + (g < b ? 6 : 0) : max == g ? (b - r) / delta + 2 : (r - g) / delta + 4;
        return (hue * 60, saturation, lightness);
    }

    static Color FromHsl(double hue, double saturation, double lightness, byte alpha)
    {
        var chroma = (1 - Math.Abs(2 * lightness - 1)) * saturation;
        var x = chroma * (1 - Math.Abs(hue / 60 % 2 - 1));
        var m = lightness - chroma / 2;
        var (r, g, b) = hue switch
        {
            < 60 => (chroma, x, 0.0),
            < 120 => (x, chroma, 0.0),
            < 180 => (0.0, chroma, x),
            < 240 => (0.0, x, chroma),
            < 300 => (x, 0.0, chroma),
            _ => (chroma, 0.0, x),
        };
        static byte Byte(double v) => (byte)Math.Round(Math.Clamp(v, 0, 1) * 255);
        return Color.FromArgb(alpha, Byte(r + m), Byte(g + m), Byte(b + m));
    }
}
