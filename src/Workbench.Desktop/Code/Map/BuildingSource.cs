using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mapsui.Projections;
using NetTopologySuite.Geometries.Prepared;
using NetTopologySuite.Operation.Linemerge;
using NetTopologySuite.Operation.Polygonize;
using NetTopologySuite.Operation.Union;
using Nts = NetTopologySuite.Geometries;

namespace Workbench.Desktop;

// One building footprint from OpenStreetMap; Shape is in Web Mercator map units, AreaSqFt is its true ground area.
public sealed record Building(long OsmId, Nts.Polygon Shape, double AreaSqFt, double Lon, double Lat);

// One OpenStreetMap way as downloaded: a building, or a named street used for the border.
public readonly record struct OsmWay(long Id, bool IsBuilding, string Name, double[] LonLat);

// What a load produced: the buildings inside the study area, its border, and a message when something went wrong.
public sealed record AreaLoad(IReadOnlyList<Building> Buildings, Nts.Polygon? Border, bool BorderFromStreets, string? Problem);

// Loads a study area (Assets/Map/study-area.json): its border streets and every building inside them.
public static class BuildingSource
{
    const double SqFtPerSqM = 10.7639;
    const int CacheTag = 0x32425756;
    static readonly string[] Endpoints = { "https://overpass-api.de/api/interpreter", "https://overpass.kumi.systems/api/interpreter" };

    public static string CacheFolder { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ValuationWorkbench", "buildings");

    // One saved file per study area; editing the streets or boxes in study-area.json gives a new name, so the area is downloaded again.
    static string CachePath(StudyArea area)
    {
        var key = string.Join("|", area.BorderStreets) + "|" + area.BuildingBox + "|" + area.StreetBox;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..10].ToLowerInvariant();
        return Path.Combine(CacheFolder, $"study-area-{hash}.bin");
    }

    // Reads the saved copy if there is one, otherwise downloads; then traces the border and keeps the buildings inside it.
    public static async Task<AreaLoad> LoadAsync(StudyArea area, IProgress<string> progress, CancellationToken ct = default)
    {
        Directory.CreateDirectory(CacheFolder);
        foreach (var old in Directory.GetFiles(CacheFolder, "area-*.bin").Append(Path.Combine(CacheFolder, "study-area.bin")).Where(File.Exists)) File.Delete(old);
        var cachePath = CachePath(area);
        var ways = File.Exists(cachePath) ? await Task.Run(() => ReadCache(cachePath), ct) : null;
        if (ways is null)
        {
            progress.Report("Downloading from OpenStreetMap…");
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("ValuationWorkbench/0.1 (student portfolio project)");
            ways = await DownloadAsync(area, http, ct);
            if (ways is null) return new AreaLoad(Array.Empty<Building>(), null, false, "Couldn't download the study area. Check your internet connection and restart the app.");
            var toSave = ways;
            await Task.Run(() => WriteCache(cachePath, toSave), ct);
        }
        else progress.Report("Loading…");
        return await Task.Run(() => Build(area, ways), ct);
    }

    // Traces the border from the streets and keeps buildings whose centre falls inside it; falls back to the box if tracing fails.
    internal static AreaLoad Build(StudyArea area, List<OsmWay> ways)
    {
        var border = TraceBorder(area, ways.Where(w => !w.IsBuilding));
        var fromStreets = border is not null;
        border ??= BoxPolygon(area.BuildingBox);
        var inside = PreparedGeometryFactory.Prepare(border);
        var buildings = new List<Building>();
        var seen = new HashSet<long>();
        foreach (var way in ways.Where(w => w.IsBuilding && seen.Add(w.Id)))
            if (ToBuilding(way.Id, way.LonLat) is { } b && inside.Contains(b.Shape.Centroid)) buildings.Add(b);
        return new AreaLoad(buildings, border, fromStreets, fromStreets ? null : "The border streets couldn't be traced, so the study area is shown as a box.");
    }

    // Joins each street's pieces, nodes all streets together, and unions the enclosed faces that sit inside the study box.
    internal static Nts.Polygon? TraceBorder(StudyArea area, IEnumerable<OsmWay> streets)
    {
        var lines = new List<Nts.Geometry>();
        foreach (var street in streets.GroupBy(s => s.Name))
        {
            var merger = new LineMerger();
            foreach (var way in street) if (way.LonLat.Length >= 4) merger.Add(ToLine(way.LonLat));
            var pieces = merger.GetMergedLineStrings().OfType<Nts.LineString>().ToList();
            lines.AddRange(pieces);
            lines.AddRange(Bridges(area, pieces));
        }
        if (lines.Count == 0) return null;
        var polygonizer = new Polygonizer();
        polygonizer.Add(UnaryUnionOp.Union(lines));
        var box = BoxPolygon(area.BuildingBox).EnvelopeInternal;
        var faces = polygonizer.GetPolygons().OfType<Nts.Polygon>().Where(p => box.Contains(p.InteriorPoint.Coordinate)).ToList();
        if (faces.Count == 0) return null;
        var union = UnaryUnionOp.Union(faces);
        var largest = Enumerable.Range(0, union.NumGeometries).Select(union.GetGeometryN).OfType<Nts.Polygon>().MaxBy(p => p.Area);
        // A border much smaller than the box means a street is missing and only a small loop closed; use the box instead.
        if (largest is null || largest.Area < box.Area * area.MinShareOfBox) return null;
        return new Nts.Polygon((Nts.LinearRing)largest.ExteriorRing.Copy());
    }

    // Straight joins between the nearest loose ends of separate pieces of one street, when the gap is short (Avery Street stops on each side of I-49).
    static IEnumerable<Nts.LineString> Bridges(StudyArea area, List<Nts.LineString> pieces)
    {
        var maxGap = area.MaxGapMeters / Math.Cos((area.BuildingBox.South + area.BuildingBox.North) / 2 * Math.PI / 180);
        for (var i = 0; i < pieces.Count; i++)
        for (var j = i + 1; j < pieces.Count; j++)
        {
            var ends = from a in new[] { pieces[i].StartPoint.Coordinate, pieces[i].EndPoint.Coordinate }
                       from b in new[] { pieces[j].StartPoint.Coordinate, pieces[j].EndPoint.Coordinate }
                       select (a, b, d: a.Distance(b));
            var closest = ends.MinBy(e => e.d);
            if (closest.d > 1 && closest.d <= maxGap) yield return new Nts.LineString(new[] { closest.a.Copy(), closest.b.Copy() });
        }
    }

    // A lon/lat box as a polygon in map units.
    static Nts.Polygon BoxPolygon(GeoBox box)
    {
        var (minX, minY) = SphericalMercator.FromLonLat(box.West, box.South);
        var (maxX, maxY) = SphericalMercator.FromLonLat(box.East, box.North);
        return new Nts.Polygon(new Nts.LinearRing(new[]
        {
            new Nts.Coordinate(minX, minY), new Nts.Coordinate(maxX, minY), new Nts.Coordinate(maxX, maxY),
            new Nts.Coordinate(minX, maxY), new Nts.Coordinate(minX, minY),
        }));
    }

    static Nts.LineString ToLine(double[] lonLat)
    {
        var coords = new Nts.Coordinate[lonLat.Length / 2];
        for (var i = 0; i < coords.Length; i++)
        {
            var (x, y) = SphericalMercator.FromLonLat(lonLat[2 * i], lonLat[2 * i + 1]);
            coords[i] = new Nts.Coordinate(x, y);
        }
        return new Nts.LineString(coords);
    }

    // One Overpass request: the border streets from the wider box, then every building in the study box.
    static async Task<List<OsmWay>?> DownloadAsync(StudyArea area, HttpClient http, CancellationToken ct)
    {
        static string Box(GeoBox b) => string.Format(CultureInfo.InvariantCulture, "{0:F4},{1:F4},{2:F4},{3:F4}", b.South, b.West, b.North, b.East);
        var names = string.Join("|", area.BorderStreets);
        var query = $"[out:json][timeout:120];way[\"highway\"][\"name\"~\"^({names})$\"]({Box(area.StreetBox)});out geom qt;"
                  + $"way[\"building\"]({Box(area.BuildingBox)});out geom qt;";
        for (var attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                using var content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("data", query) });
                using var response = await http.PostAsync(Endpoints[attempt % Endpoints.Length], content, ct);
                if (response.IsSuccessStatusCode)
                {
                    await using var stream = await response.Content.ReadAsStreamAsync(ct);
                    return await Task.Run(() => Parse(stream), ct);
                }
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException && !ct.IsCancellationRequested) { }
            await Task.Delay(TimeSpan.FromSeconds(5 * (attempt + 1)), ct);
        }
        return null;
    }

    // Reads an Overpass JSON answer; a server-side error in the answer counts as a failure.
    internal static List<OsmWay> Parse(Stream json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("remark", out var remark) && remark.GetString() is { } text && text.Contains("error", StringComparison.OrdinalIgnoreCase))
            throw new JsonException("Overpass returned an error: " + text);
        var ways = new List<OsmWay>();
        foreach (var e in root.GetProperty("elements").EnumerateArray())
        {
            if (e.GetProperty("type").GetString() != "way" || !e.TryGetProperty("geometry", out var geometry)) continue;
            var tags = e.TryGetProperty("tags", out var t) ? t : default;
            var isBuilding = tags.ValueKind == JsonValueKind.Object && tags.TryGetProperty("building", out _);
            var name = tags.ValueKind == JsonValueKind.Object && tags.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            var lonLat = new double[geometry.GetArrayLength() * 2];
            var i = 0;
            var complete = true;
            foreach (var p in geometry.EnumerateArray())
            {
                if (p.ValueKind != JsonValueKind.Object) { complete = false; break; }
                lonLat[i++] = p.GetProperty("lon").GetDouble();
                lonLat[i++] = p.GetProperty("lat").GetDouble();
            }
            if (complete) ways.Add(new OsmWay(e.GetProperty("id").GetInt64(), isBuilding, name, lonLat));
        }
        return ways;
    }

    // Turns lon/lat pairs into a Web Mercator polygon; returns null for shapes with fewer than three corners.
    internal static Building? ToBuilding(long id, double[] lonLat)
    {
        var n = lonLat.Length / 2;
        if (n < 3) return null;
        var closed = lonLat[0] == lonLat[^2] && lonLat[1] == lonLat[^1];
        var coords = new Nts.Coordinate[closed ? n : n + 1];
        for (var i = 0; i < n; i++)
        {
            var (x, y) = SphericalMercator.FromLonLat(lonLat[2 * i], lonLat[2 * i + 1]);
            coords[i] = new Nts.Coordinate(x, y);
        }
        if (!closed) coords[n] = coords[0].Copy();
        if (coords.Length < 4) return null;
        var shape = new Nts.Polygon(new Nts.LinearRing(coords));
        var centre = shape.EnvelopeInternal.Centre;
        var (lon, lat) = SphericalMercator.ToLonLat(centre.X, centre.Y);
        // Web Mercator stretches lengths by 1 / cos(latitude), so areas shrink back by cos squared.
        var cos = Math.Cos(lat * Math.PI / 180);
        return new Building(id, shape, shape.Area * cos * cos * SqFtPerSqM, lon, lat);
    }

    // Saved format: a 4-byte tag, the way count, then each way's id, kind, name, point count and lon/lat doubles.
    internal static void WriteCache(string path, List<OsmWay> ways)
    {
        var temp = path + ".tmp";
        using (var writer = new BinaryWriter(File.Create(temp)))
        {
            writer.Write(CacheTag);
            writer.Write(ways.Count);
            foreach (var way in ways)
            {
                writer.Write(way.Id);
                writer.Write(way.IsBuilding);
                writer.Write(way.Name);
                writer.Write(way.LonLat.Length);
                foreach (var v in way.LonLat) writer.Write(v);
            }
        }
        File.Move(temp, path, overwrite: true);
    }

    // Returns null when the saved file is missing, from an older version or damaged, so the area gets downloaded again.
    internal static List<OsmWay>? ReadCache(string path)
    {
        try
        {
            using var reader = new BinaryReader(File.OpenRead(path));
            if (reader.ReadInt32() != CacheTag) return null;
            var count = reader.ReadInt32();
            var ways = new List<OsmWay>(count);
            for (var w = 0; w < count; w++)
            {
                var id = reader.ReadInt64();
                var isBuilding = reader.ReadBoolean();
                var name = reader.ReadString();
                var lonLat = new double[reader.ReadInt32()];
                for (var i = 0; i < lonLat.Length; i++) lonLat[i] = reader.ReadDouble();
                ways.Add(new OsmWay(id, isBuilding, name, lonLat));
            }
            return ways;
        }
        catch (Exception e) when (e is IOException or EndOfStreamException or OverflowException or OutOfMemoryException) { return null; }
    }
}
