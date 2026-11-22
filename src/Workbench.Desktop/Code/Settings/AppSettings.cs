using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Workbench.Desktop;

public enum AppTheme { Light, Dark }

// Per-user preferences, kept in %LOCALAPPDATA%\ValuationWorkbench\settings.json.
public sealed record AppSettings(AppTheme Theme, bool ReduceMotion = false, string ProfileColor = "blue", string CodeTheme = "app")
{
    static readonly string FilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ValuationWorkbench", "settings.json");
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    public static AppSettings Load()
    {
        try { return File.Exists(FilePath) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Json) ?? new(AppTheme.Light) : new(AppTheme.Light); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return new(AppTheme.Light); }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
    }
}
