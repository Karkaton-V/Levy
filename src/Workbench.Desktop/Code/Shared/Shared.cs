using System.Globalization;
using System.IO;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using Workbench.Engine;

namespace Workbench.Desktop;

// How a parcel's assessment ratio compares with the target range; its colour and label come from Assets/Map/map-style.json.
public enum RatioCategory { Under, InRange, Over, NoSale }

// Everything used by more than one file in this project lives here: asset loading, ratio categories, formatting, rule lookups and command helpers.
public static class Shared
{
    static readonly CultureInfo Us = CultureInfo.GetCultureInfo("en-US");
    static readonly Lazy<IHighlightingDefinition> LightSyntax = new(() => LoadSyntax("Rules.Light.xshd"));
    static readonly Lazy<IHighlightingDefinition> DarkSyntax = new(() => LoadSyntax("Rules.Dark.xshd"));

    // Full path of a file in the Assets folder that the build copies next to the app, e.g. AssetPath("Map", "study-area.json").
    public static string AssetPath(params string[] parts) => Path.Combine(new[] { AppContext.BaseDirectory, "Assets" }.Concat(parts).ToArray());

    // The text of an asset file.
    public static string ReadAsset(params string[] parts) => File.ReadAllText(AssetPath(parts));

    // Syntax colours for the rules language from Assets/Shared/Rules.Light.xshd or Rules.Dark.xshd, each loaded once; used by the Editor and the Map's code snippets.
    public static IHighlightingDefinition RulesSyntax(bool dark) => dark ? DarkSyntax.Value : LightSyntax.Value;

    static IHighlightingDefinition LoadSyntax(string file, string folder = "Shared")
    {
        using var reader = XmlReader.Create(AssetPath(folder, file));
        return HighlightingLoader.Load(reader, HighlightingManager.Instance);
    }

    static readonly Dictionary<string, IHighlightingDefinition> CodeSyntaxes = [];

    // Colours a rules editor in a code theme: "app" uses Assets/Editor/Colors.*.xaml and Rules.Light/Dark.xshd for the current app theme;
    // a fixed theme uses its Code<Name>* colours and Assets/Editor/Rules.<Name>.xshd. Call again on ThemeManager.Changed.
    public static void PaintCode(TextEditor editor, string theme)
    {
        var name = theme.Length > 0 ? char.ToUpperInvariant(theme[0]) + theme[1..] : "App";
        var fixedTheme = name != "App" && editor.TryFindResource($"Code{name}Background") is Brush;
        string Key(string part, string appKey) => fixedTheme ? $"Code{name}{part}" : appKey;
        if (fixedTheme) editor.SetResourceReference(Control.BackgroundProperty, Key("Background", ""));
        else editor.Background = Brushes.Transparent;
        editor.SetResourceReference(Control.ForegroundProperty, Key("Text", "Text"));
        editor.SetResourceReference(TextEditor.LineNumbersForegroundProperty, Key("LineNumbers", "EditorLineNumbers"));
        editor.TextArea.TextView.CurrentLineBackground = (Brush)editor.FindResource(Key("CurrentLine", "EditorCurrentLine"));
        editor.TextArea.TextView.CurrentLineBorder = (Pen)editor.FindResource("EditorCurrentLineBorder");
        editor.TextArea.SelectionBrush = (Brush)editor.FindResource(Key("Selection", "EditorSelection"));
        editor.TextArea.Caret.CaretBrush = (Brush)editor.FindResource(Key("Caret", "EditorCaret"));
        if (!fixedTheme) editor.SyntaxHighlighting = RulesSyntax(ThemeManager.IsDark);
        else
        {
            if (!CodeSyntaxes.TryGetValue(name, out var syntax)) CodeSyntaxes[name] = syntax = LoadSyntax($"Rules.{name}.xshd", "Editor");
            editor.SyntaxHighlighting = syntax;
        }
    }

    // Which side of the 0.95 to 1.05 target a parcel's ratio falls on.
    public static RatioCategory CategoryOf(double? ratio) => ratio switch
    {
        null => RatioCategory.NoSale,
        < RatioStudy.MedianLow => RatioCategory.Under,
        > RatioStudy.MedianHigh => RatioCategory.Over,
        _ => RatioCategory.InRange,
    };

    // Whole-dollar money, e.g. $145,700; negatives get a minus sign in front.
    public static string Money(decimal amount) => amount < 0 ? "−" + (-amount).ToString("C0", Us) : amount.ToString("C0", Us);

    // Money with an explicit plus or minus, for changes.
    public static string Signed(decimal amount) => (amount >= 0 ? "+" : "−") + Math.Abs(amount).ToString("C0", Us);

    // A number in US formatting, e.g. Number(0.9871, "F3") gives 0.987.
    public static string Number(double value, string format) => value.ToString(format, Us);

    // Line number (1-based) of each rule name in the rules text; the first line wins if a name repeats.
    public static Dictionary<string, int> RuleLines(string text)
    {
        var lines = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var rows = text.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < rows.Length; i++)
        {
            var row = rows[i].Trim();
            var equals = row.IndexOf('=');
            if (row.StartsWith('#') || equals <= 0) continue;
            lines.TryAdd(row[..equals].Trim(), i + 1);
        }
        return lines;
    }

    // Plain-English description of a rule name, or null when the name is not a known rule.
    public static string? DescribeRule(string key)
    {
        if (key.StartsWith("LandRate.", StringComparison.OrdinalIgnoreCase)) return RuleSet.Known[0].Description;
        return RuleSet.Known.FirstOrDefault(k => k.Key.Equals(key, StringComparison.OrdinalIgnoreCase))?.Description;
    }
}

// Visible when false, collapsed when true: for "nothing here yet" messages beside a list.
public sealed class InverseBoolVis : System.Windows.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
        value is true ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
}

// Runs a plain Action from a button or key binding.
public sealed class RelayCommand(Action execute) : ICommand
{
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => execute();
}

// Runs an Action that takes the button's CommandParameter.
public sealed class ParameterCommand(Action<object?> execute) : ICommand
{
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => execute(parameter);
}
