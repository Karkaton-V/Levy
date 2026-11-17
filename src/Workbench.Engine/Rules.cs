using System.Globalization;

namespace Workbench.Engine;

// A rule name the editor can suggest, with a plain-English description for hover and completion.
public sealed record RuleInfo(string Key, string Description);

// The editable valuation rules: parsed from "Key = value" lines in the editor.
public sealed class RuleSet
{
    // Every rule name the parser understands; LandRate takes a neighborhood after the dot.
    public static IReadOnlyList<RuleInfo> Known { get; } = new RuleInfo[]
    {
        new("LandRate.<neighborhood>", "Land value in dollars per square foot of lot, for one neighborhood."),
        new("CostPerSqFt", "Cost to build one square foot of living area new, in dollars."),
        new("DepreciationPerYear", "Share of building value lost per year of age; 0.009 is 0.9% a year."),
        new("MaxDepreciation", "Most a building can lose to age; 0.55 means it keeps at least 45% of its cost."),
        new("ValuationYear", "The year values are set for; building age is counted up to this year."),
        new("AssessmentRatio", "Share of market value that is assessed; Louisiana homes use 0.10 (10%)."),
        new("HomesteadExemption", "Assessed value exempt for an owner-occupied home, in dollars; Louisiana's is 7,500."),
        new("Millage", "Tax rate in mills: dollars of tax per $1,000 of taxable value."),
    };

    public Dictionary<string, decimal> LandRates { get; } = new(StringComparer.OrdinalIgnoreCase);
    public decimal CostPerSqFt { get; private set; } = 112m;
    public decimal DepreciationPerYear { get; private set; } = 0.009m;
    public decimal MaxDepreciation { get; private set; } = 0.55m;
    public int ValuationYear { get; private set; } = 2026;
    public decimal AssessmentRatio { get; private set; } = 0.10m;
    public decimal HomesteadExemption { get; private set; } = 7500m;
    public decimal Millage { get; private set; } = 120m;

    // The rules written out as plain sentences, for readers who don't want the code; problem lines are called out at the end.
    public string ToPlainWords(IReadOnlyList<Diagnostic> problems)
    {
        static string Money(decimal v, string format = "N0") => "$" + v.ToString(format, CultureInfo.InvariantCulture);
        static string Percent(decimal share) => (share * 100).ToString("0.##", CultureInfo.InvariantCulture) + "%";
        var lines = new List<string> { "How parcels are valued", "", "LAND" };
        if (LandRates.Count == 0) lines.Add("• No land rates are set, so land is valued at $0 everywhere.");
        foreach (var (neighborhood, rate) in LandRates.OrderBy(r => r.Key, StringComparer.OrdinalIgnoreCase))
            lines.Add($"• Land in neighborhood {neighborhood} is worth {Money(rate, "N2")} per square foot of lot.");
        lines.AddRange(new[]
        {
            "", "BUILDINGS",
            $"• A building is first valued as if new, at {Money(CostPerSqFt, "N2")} per square foot of living area.",
            $"• It then loses {Percent(DepreciationPerYear)} of that value for each year of age, counting up to {ValuationYear}.",
            $"• It never loses more than {Percent(MaxDepreciation)}, so every building keeps at least {Percent(1 - Math.Min(1, MaxDepreciation))} of its new value.",
            "", "MARKET VALUE",
            "• Market value is the land value plus the building value, rounded to the nearest $100.",
            "", "TAX",
            $"• The assessed value is {Percent(AssessmentRatio)} of market value.",
            $"• An owner-occupied home has the first {Money(HomesteadExemption)} of assessed value exempt (the homestead exemption).",
            $"• The tax rate is {Millage.ToString("0.##", CultureInfo.InvariantCulture)} mills: {Money(Millage, "N2")} of tax for every $1,000 of taxable value.",
        });
        if (problems.Count > 0)
        {
            lines.AddRange(new[] { "", "NOT APPLIED" });
            lines.AddRange(problems.Select(p => p.Line > 0 ? $"• Line {p.Line}: {p.Message} The default is used instead." : $"• {p.Message}"));
        }
        return string.Join(Environment.NewLine, lines);
    }

    // Reads the rules text; bad lines become diagnostics and keep their default value.
    public static RuleSet Parse(string text, out List<Diagnostic> diagnostics)
    {
        var rules = new RuleSet();
        diagnostics = new List<Diagnostic>();
        var lines = text.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var parts = line.Split('=', 2);
            if (parts.Length != 2) { diagnostics.Add(new Diagnostic(i + 1, "Expected \"Key = value\".")); continue; }
            var key = parts[0].Trim();
            if (!decimal.TryParse(parts[1].Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var value)) { diagnostics.Add(new Diagnostic(i + 1, $"\"{parts[1].Trim()}\" is not a number.")); continue; }
            if (value < 0) { diagnostics.Add(new Diagnostic(i + 1, $"{key} cannot be negative.")); continue; }
            if (key.StartsWith("LandRate.", StringComparison.OrdinalIgnoreCase) && key.Length > 9) { rules.LandRates[key[9..]] = value; continue; }
            switch (key.ToLowerInvariant())
            {
                case "costpersqft": rules.CostPerSqFt = value; break;
                case "depreciationperyear": rules.DepreciationPerYear = value; break;
                case "maxdepreciation": rules.MaxDepreciation = value; break;
                case "valuationyear": rules.ValuationYear = (int)value; break;
                case "assessmentratio": rules.AssessmentRatio = value; break;
                case "homesteadexemption": rules.HomesteadExemption = value; break;
                case "millage": rules.Millage = value; break;
                default: diagnostics.Add(new Diagnostic(i + 1, $"Unknown rule \"{key}\".")); break;
            }
        }
        return rules;
    }
}

// Applies a RuleSet to parcels and records each step so the UI can explain the value.
public static class Valuer
{
    public static Valuation Value(Parcel parcel, RuleSet rules)
    {
        rules.LandRates.TryGetValue(parcel.Neighborhood, out var rate);
        var age = Math.Max(0, rules.ValuationYear - parcel.YearBuilt);
        var depreciation = Math.Min(rules.MaxDepreciation, age * rules.DepreciationPerYear);
        var land = parcel.LotSqFt * rate;
        var building = parcel.LivingSqFt * rules.CostPerSqFt * (1 - depreciation);
        var market = Math.Round((land + building) / 100m) * 100m;
        var assessed = market * rules.AssessmentRatio;
        var exemption = Math.Min(rules.HomesteadExemption, assessed);
        var taxable = assessed - exemption;
        var tax = taxable * rules.Millage / 1000m;
        var trace = new List<TraceStep>
        {
            new("LandValue", $"{parcel.LotSqFt:N0} sq ft x ${rate:0.00} per sq ft", land, new[] { "LandRate." + parcel.Neighborhood },
                $"LotSqFt × LandRate.{parcel.Neighborhood}"),
            new("BuildingValue", $"{parcel.LivingSqFt:N0} sq ft x ${rules.CostPerSqFt:0.##}, less {depreciation * 100:0}% depreciation", building, new[] { "CostPerSqFt", "DepreciationPerYear", "MaxDepreciation", "ValuationYear" },
                "LivingSqFt × CostPerSqFt × (1 − min(MaxDepreciation, (ValuationYear − YearBuilt) × DepreciationPerYear))"),
            new("MarketValue", "Land plus building, rounded to $100", market, Array.Empty<string>(),
                "round(LandValue + BuildingValue, to the nearest $100)"),
            new("Assessed", $"{rules.AssessmentRatio * 100:0.##}% of market value", assessed, new[] { "AssessmentRatio" },
                "MarketValue × AssessmentRatio"),
            new("Homestead", $"Exemption, up to ${rules.HomesteadExemption:N0}", -exemption, new[] { "HomesteadExemption" },
                "− min(HomesteadExemption, Assessed)"),
            new("EstimatedTax", $"${taxable:N0} taxable at {rules.Millage:0.##} mills", tax, new[] { "Millage" },
                "(Assessed − Homestead) × Millage ÷ 1,000"),
        };
        return new Valuation(parcel, land, building, market, assessed, taxable, tax, trace);
    }

    // Values every parcel and reports neighborhoods that have no land rate.
    public static List<Valuation> ValueAll(IEnumerable<Parcel> parcels, RuleSet rules, List<Diagnostic> diagnostics)
    {
        var list = parcels.ToList();
        foreach (var missing in list.Select(p => p.Neighborhood).Distinct().Where(n => !rules.LandRates.ContainsKey(n)))
            diagnostics.Add(new Diagnostic(0, $"No LandRate for neighborhood {missing}; its land is valued at $0."));
        return list.Select(p => Value(p, rules)).ToList();
    }
}
