using Workbench.Engine;
using Xunit;

namespace Workbench.Engine.Tests;

public class EngineTests
{
    // The same starting rules the app loads: Assets/Editor/DefaultRules.rules, linked into this project's output.
    static readonly string DefaultRules = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "DefaultRules.rules"));

    [Fact]
    public void RatioStudy_computes_median_cod_and_prd()
    {
        var stats = RatioStudy.FromPairs(new[] { (90_000m, 100_000m), (100_000m, 100_000m), (110_000m, 100_000m) });
        Assert.Equal(3, stats.Count);
        Assert.Equal(1.0, stats.Median, 3);
        Assert.Equal(6.667, stats.Cod, 3);
        Assert.Equal(1.0, stats.Prd, 3);
    }

    [Fact]
    public void Default_rules_parse_without_diagnostics()
    {
        var rules = RuleSet.Parse(DefaultRules, out var diagnostics);
        Assert.Empty(diagnostics);
        Assert.Equal(9.00m, rules.LandRates["N-04"]);
        Assert.Equal(120m, rules.Millage);
    }

    [Fact]
    public void Bad_lines_report_their_line_number()
    {
        RuleSet.Parse("Millage = 120\nCostPerSqFt = lots\nNotARule = 1", out var diagnostics);
        Assert.Equal(new[] { 2, 3 }, diagnostics.Select(d => d.Line));
    }

    [Fact]
    public void Valuer_applies_cost_approach_and_homestead()
    {
        var rules = RuleSet.Parse(DefaultRules, out _);
        var parcel = new Parcel(0, "04-0001", "N-04", 8000, 2000, 2016, null);
        var v = Valuer.Value(parcel, rules);
        Assert.Equal(72_000m, v.LandValue);
        Assert.Equal(203_840m, v.BuildingValue);
        Assert.Equal(275_800m, v.MarketValue);
        Assert.Equal(27_580m, v.AssessedValue);
        Assert.Equal(20_080m, v.TaxableValue);
        Assert.Equal(2_409.6m, v.EstimatedTax);
    }
}
