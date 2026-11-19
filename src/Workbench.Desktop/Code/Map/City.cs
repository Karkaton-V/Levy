using System.IO;
using System.Text.Json;

namespace Workbench.Desktop;

// A city marker on the Louisiana overview, read from Assets/Map/cities.json; HasStudyArea marks the city the study area is in.
public sealed record City(string Name, double Lat, double Lon, bool HasStudyArea)
{
    static readonly Lazy<IReadOnlyList<City>> Loaded = new(() => Parse(Shared.ReadAsset("Map", "cities.json")));
    static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    public static IReadOnlyList<City> All => Loaded.Value;

    public static IReadOnlyList<City> Parse(string json) => JsonSerializer.Deserialize<List<City>>(json, Options) ?? throw new InvalidDataException("cities.json is empty.");
}
