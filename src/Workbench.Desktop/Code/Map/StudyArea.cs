using System.IO;
using System.Text.Json;

namespace Workbench.Desktop;

// A latitude and longitude box.
public sealed record GeoBox(double South, double West, double North, double East);

// How many sample parcels to value, and which building sizes count as houses.
public sealed record SampleRules(int Count, double MinHouseSqFt, double MaxHouseSqFt);

// The area the map loads, read from Assets/Map/study-area.json: the streets that border it, the boxes to download, and the sample rules.
public sealed record StudyArea(
    string Name, string Description, string[] BorderStreets, GeoBox BuildingBox, GeoBox StreetBox,
    double MaxGapMeters, double MinShareOfBox, SampleRules Sample)
{
    static readonly Lazy<StudyArea> Loaded = new(() => Parse(Shared.ReadAsset("Map", "study-area.json")));
    static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    public static StudyArea Default => Loaded.Value;

    public static StudyArea Parse(string json) => JsonSerializer.Deserialize<StudyArea>(json, Options) ?? throw new InvalidDataException("study-area.json is empty.");
}
