using System.Windows;

namespace Workbench.Desktop;

// Loads Assets/<folder>/Colors.Light.xaml or Colors.Dark.xaml into the app's resources and swaps them on demand.
// Styles look colours up with DynamicResource, so everything open recolours at once; code that copies colours listens to Changed.
public static class ThemeManager
{
    static readonly string[] Folders = ["Shared", "App", "Editor", "Map", "Compare", "Settings"];
    static readonly List<ResourceDictionary> Loaded = [];

    public static AppTheme Current { get; private set; } = AppTheme.Light;
    public static bool IsDark => Current == AppTheme.Dark;
    public static event Action? Changed;

    // Applies the saved theme; call once at startup, before any window opens.
    public static void Initialize() => Apply(AppSettings.Load().Theme, save: false);

    public static void Apply(AppTheme theme, bool save = true)
    {
        var merged = Application.Current.Resources.MergedDictionaries;
        foreach (var dictionary in Loaded) merged.Remove(dictionary);
        Loaded.Clear();
        foreach (var folder in Folders)
        {
            var colors = new ResourceDictionary { Source = new Uri($"/ValuationWorkbench;component/Assets/{folder}/Colors.{theme}.xaml", UriKind.Relative) };
            merged.Insert(Loaded.Count, colors);
            Loaded.Add(colors);
        }
        Current = theme;
        if (save) (AppSettings.Load() with { Theme = theme }).Save();
        Changed?.Invoke();
    }
}
