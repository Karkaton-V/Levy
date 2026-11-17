namespace Workbench.Engine;

// Made-up parcels so the app runs before real county data is loaded; the same seed gives the same parcels every time.
public static class SampleData
{
    // Pass livingSqFt to use real building sizes (one parcel per entry); otherwise sizes are made up.
    public static IReadOnlyList<Parcel> Create(IReadOnlyList<int>? livingSqFt = null, int count = 96)
    {
        count = livingSqFt?.Count ?? count;
        long seed = 11;
        double Next() { seed = seed * 16807 % 2147483647; return (seed - 1) / 2147483646.0; }
        var parcels = new List<Parcel>(count);
        for (var i = 0; i < count; i++)
        {
            var lot = 5600 + (int)(Next() * 48) * 100;
            var madeUpLiving = 1100 + (int)(Next() * 190) * 10;
            var living = livingSqFt?[i] ?? madeUpLiving;
            var year = 1958 + (int)(Next() * 62);
            var noise = 0.88 + Next() * 0.24;
            var sold = Next() < 0.5;
            var neighborhood = i < count / 2 ? "N-04" : "N-07";
            // The "true" market pays more for N-04 land than the starting rules assume, so N-04 starts out under-assessed.
            var trueLandRate = neighborhood == "N-04" ? 12m : 6m;
            var building = living * 112m * (1 - Math.Min(0.55m, (2026 - year) * 0.009m));
            decimal? sale = sold ? Math.Round((lot * trueLandRate + building) * (decimal)noise / 500m) * 500m : null;
            var id = (neighborhood == "N-04" ? "04-" : "07-") + (2100 + i * 3);
            parcels.Add(new Parcel(i, id, neighborhood, lot, living, year, sale));
        }
        return parcels;
    }
}
