using Workbench.Engine;
using static Workbench.Desktop.Shared;

namespace Workbench.Desktop;

// One pair of histogram bars; InTarget marks bins inside the 0.95 to 1.05 target.
public sealed record BinBar(string Label, double BeforeHeight, double AfterHeight, bool InTarget, string Tip);

// One row of the tax change table.
public sealed record TaxChangeRow(string Id, string ValueBefore, string ValueAfter, string TaxBefore, string TaxAfter, string Change);

// The Compare tab's part of the view model: the last two runs side by side.
public sealed partial class MainViewModel
{
    // Tallest bar on the chart, in pixels.
    const double MaxBarHeight = 130;

    public bool HasComparison { get; private set; }
    public bool NoComparison => !HasComparison;
    public string CompareTitle { get; private set; } = "";
    public string CompareSummary { get; private set; } = "";
    public IReadOnlyList<BinBar> Bins { get; private set; } = Array.Empty<BinBar>();
    public IReadOnlyList<TaxChangeRow> TaxChanges { get; private set; } = Array.Empty<TaxChangeRow>();

    // After a run: the before/after histogram and the biggest tax bill changes.
    void UpdateCompareAfterRun()
    {
        HasComparison = _previous is not null;
        if (_previous is null) return;
        CompareTitle = $"Run {_runNumber - 1} compared with run {_runNumber}";
        int[] a = RatioStudy.Histogram(_previous), b = RatioStudy.Histogram(_current);
        double max = Math.Max(1, a.Concat(b).Max());
        Bins = a.Select((count, k) => new BinBar(Number(0.80 + k * 0.05, "F2"), count / max * MaxBarHeight, b[k] / max * MaxBarHeight, k is 3 or 4, $"Before: {count} parcels, after: {b[k]} parcels")).ToList();

        var changes = _previous.Zip(_current, (p, c) => (Before: p, After: c)).Where(x => x.Before.MarketValue != x.After.MarketValue)
            .OrderByDescending(x => Math.Abs(x.After.EstimatedTax - x.Before.EstimatedTax)).ToList();
        TaxChanges = changes.Take(8).Select(x => new TaxChangeRow(x.Before.Parcel.Id, Money(x.Before.MarketValue), Money(x.After.MarketValue), Money(x.Before.EstimatedTax), Money(x.After.EstimatedTax), Signed(x.After.EstimatedTax - x.Before.EstimatedTax))).ToList();
        if (changes.Count == 0) { CompareSummary = "No parcel changed value between these two runs."; return; }
        var deltas = changes.Select(x => x.After.EstimatedTax - x.Before.EstimatedTax).OrderBy(d => d).ToList();
        CompareSummary = $"{changes.Count} parcels changed value. Median tax change for those: {Signed(deltas[deltas.Count / 2])} per year.";
    }
}
