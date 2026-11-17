using System.Globalization;
using System.IO;
using System.Text.Json;

namespace Workbench.Desktop;

// A fill and outline from Assets/Map/map-style.json; colours are "#RRGGBB" or "#AARRGGBB", and Label is shown in the legend.
public sealed record AreaLook(string? Label, string? Fill, string? Outline, double OutlineWidth);

// A line with an optional wider casing drawn underneath it.
public sealed record LineLook(string? Label, string Line, double LineWidth, string? Casing, double CasingWidth);

// Every colour, width and legend label the map uses, read from Assets/Map/map-style.json.
public sealed record MapStyle(
    AreaLook Under, AreaLook InRange, AreaLook Over, AreaLook NoSale, AreaLook ValuedOutline,
    AreaLook Empty, AreaLook Hover, AreaLook Selection, LineLook Border, AreaLook OutsideLouisiana,
    double MaskMarginDegrees, double EmptyParcelsMaxResolution)
{
    static readonly Lazy<MapStyle> Loaded = new(() => Parse(Shared.ReadAsset("Map", "map-style.json")));
    static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    public static MapStyle Default => Loaded.Value;

    public static MapStyle Parse(string json) => JsonSerializer.Deserialize<MapStyle>(json, Options) ?? throw new InvalidDataException("map-style.json is empty.");

    // The look for one ratio category.
    public AreaLook Look(RatioCategory category) => category switch
    {
        RatioCategory.Under => Under,
        RatioCategory.InRange => InRange,
        RatioCategory.Over => Over,
        _ => NoSale,
    };

    // Lower-case wording for hover text, taken from the category's legend label.
    public string Word(RatioCategory category) => (Look(category).Label ?? category.ToString()).ToLowerInvariant();

    // Splits "#RRGGBB" or "#AARRGGBB" into alpha, red, green and blue.
    public static (byte A, byte R, byte G, byte B) Argb(string hex)
    {
        var digits = hex.TrimStart('#');
        var value = uint.Parse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        if (digits.Length == 6) value |= 0xFF000000;
        else if (digits.Length != 8) throw new FormatException($"\"{hex}\" is not a #RRGGBB or #AARRGGBB colour.");
        return ((byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value);
    }
}
