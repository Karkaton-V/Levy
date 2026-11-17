namespace Workbench.Engine;

// One property on the roll; SalePrice is null when there is no recent sale.
public sealed record Parcel(int Index, string Id, string Neighborhood, int LotSqFt, int LivingSqFt, int YearBuilt, decimal? SalePrice);

// One line of the "why this value" explanation; Keys are the rule names it used and Formula shows how they combine.
public sealed record TraceStep(string Rule, string Detail, decimal Amount, IReadOnlyList<string> Keys, string Formula);

// A problem found in the rules text or while valuing; Line is 0 when it has no line.
public sealed record Diagnostic(int Line, string Message);

// The result of running the rules against one parcel.
public sealed record Valuation(Parcel Parcel, decimal LandValue, decimal BuildingValue, decimal MarketValue, decimal AssessedValue, decimal TaxableValue, decimal EstimatedTax, IReadOnlyList<TraceStep> Trace)
{
    // Estimated market value divided by sale price; null when the parcel has no sale.
    public double? Ratio => Parcel.SalePrice is { } sale && sale > 0 ? (double)(MarketValue / sale) : null;
}

// Ratio study results for a group of sold parcels.
public sealed record RatioStats(int Count, double Median, double Cod, double Prd);
