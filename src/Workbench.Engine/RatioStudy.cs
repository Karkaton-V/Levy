namespace Workbench.Engine;

// Standard assessment-quality statistics, compared against commonly used IAAO target ranges.
public static class RatioStudy
{
    public const double MedianLow = 0.95, MedianHigh = 1.05, CodMax = 15.0, PrdLow = 0.98, PrdHigh = 1.03;

    public static bool MedianOk(double median) => median >= MedianLow && median <= MedianHigh;
    public static bool CodOk(double cod) => cod <= CodMax;
    public static bool PrdOk(double prd) => prd >= PrdLow && prd <= PrdHigh;

    // Ratio study over the parcels that have a sale.
    public static RatioStats Compute(IEnumerable<Valuation> valuations) =>
        FromPairs(valuations.Where(v => v.Ratio is not null).Select(v => (v.MarketValue, v.Parcel.SalePrice!.Value)));

    // Median ratio, COD (average % deviation from the median) and PRD (mean ratio / value-weighted mean ratio).
    public static RatioStats FromPairs(IEnumerable<(decimal Market, decimal Sale)> pairs)
    {
        var list = pairs.Where(p => p.Sale > 0).ToList();
        if (list.Count == 0) return new RatioStats(0, 0, 0, 0);
        var ratios = list.Select(p => (double)(p.Market / p.Sale)).OrderBy(r => r).ToList();
        var mid = ratios.Count / 2;
        var median = ratios.Count % 2 == 1 ? ratios[mid] : (ratios[mid - 1] + ratios[mid]) / 2;
        var cod = 100 * ratios.Average(r => Math.Abs(r - median)) / median;
        var weightedMean = (double)(list.Sum(p => p.Market) / list.Sum(p => p.Sale));
        var prd = ratios.Average() / weightedMean;
        return new RatioStats(ratios.Count, median, cod, prd);
    }

    // Counts ratios into equal-width bins; values outside the range land in the first or last bin.
    public static int[] Histogram(IEnumerable<Valuation> valuations, double low = 0.80, double width = 0.05, int bins = 8)
    {
        var counts = new int[bins];
        foreach (var ratio in valuations.Select(v => v.Ratio).OfType<double>())
            counts[Math.Clamp((int)Math.Floor((ratio - low) / width), 0, bins - 1)]++;
        return counts;
    }
}
