using System.IO;
using System.Text.Json;

namespace Workbench.Desktop;

// Louisiana's outline as lon, lat pairs plus its bounding box, read from Assets/Map/louisiana.geojson.
public sealed record LouisianaOutline(double[] LonLat, double West, double South, double East, double North)
{
    static readonly Lazy<LouisianaOutline> Loaded = new(() => Parse(Shared.ReadAsset("Map", "louisiana.geojson")));

    public static LouisianaOutline Default => Loaded.Value;

    // Reads the outer ring of a GeoJSON Polygon, either bare or inside a Feature.
    public static LouisianaOutline Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var geometry = doc.RootElement.TryGetProperty("geometry", out var g) ? g : doc.RootElement;
        if (geometry.GetProperty("type").GetString() != "Polygon") throw new InvalidDataException("louisiana.geojson must hold a Polygon.");
        var ring = geometry.GetProperty("coordinates")[0];
        var lonLat = ring.EnumerateArray().SelectMany(p => new[] { p[0].GetDouble(), p[1].GetDouble() }).ToArray();
        var lons = lonLat.Where((_, i) => i % 2 == 0).ToArray();
        var lats = lonLat.Where((_, i) => i % 2 == 1).ToArray();
        return new LouisianaOutline(lonLat, lons.Min(), lats.Min(), lons.Max(), lats.Max());
    }
}
