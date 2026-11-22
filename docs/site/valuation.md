---
layout: page
title: Valuation
permalink: /valuation/
---

The valuation code is in `src/Workbench.Engine`, a plain .NET 8 library with no UI. The desktop app's **Rules**, **Parcel map** and **Compare runs** pages sit on top of it.

## The rules language
Rules are `Key = value` lines. Blank lines and lines starting with `#` are ignored. These are the starting rules:

```
# Land rate in dollars per square foot, by neighborhood
LandRate.N-04 = 9.00
LandRate.N-07 = 6.00

# Building cost approach
CostPerSqFt = 112
DepreciationPerYear = 0.009
MaxDepreciation = 0.55
ValuationYear = 2026

# Louisiana-style tax math (sample millage)
AssessmentRatio = 0.10
HomesteadExemption = 7500
Millage = 120
```

| Rule | Meaning |
|---|---|
| `LandRate.<neighborhood>` | Land value per square foot of lot, for one neighborhood. |
| `CostPerSqFt` | Cost to build one square foot of living area new. |
| `DepreciationPerYear` | Share of building value lost per year of age (0.009 is 0.9% a year). |
| `MaxDepreciation` | The most a building can lose to age (0.55 keeps at least 45%). |
| `ValuationYear` | The year values are set for; age counts up to it. |
| `AssessmentRatio` | Share of market value that's assessed (Louisiana homes use 10%). |
| `HomesteadExemption` | Assessed value exempt for an owner-occupied home (Louisiana's is $7,500). |
| `Millage` | Tax rate in mills: dollars of tax per $1,000 of taxable value. |

`RuleSet.Parse` reads the text. A bad line (no `=`, not a number, a negative value, an unknown key) becomes a diagnostic with its line number and keeps the default value, so a typo never stops a run. A neighborhood with no `LandRate` gets a warning and $0 land.

**The editor** (AvalonEdit) colours the syntax, underlines problems as you type, shows hover help and completion from the same rule list, and runs on F5. The rules can also be shown as plain sentences for readers who don't want the code.

## The valuer
`Valuer.Value` applies the rules to one parcel with a cost approach:

1. **Land** = lot square feet × `LandRate` for its neighborhood.
2. **Building** = living square feet × `CostPerSqFt` × (1 − depreciation), where depreciation = min(`MaxDepreciation`, age × `DepreciationPerYear`).
3. **Market value** = land + building, rounded to the nearest $100.
4. **Assessed** = market value × `AssessmentRatio`.
5. **Homestead** = up to `HomesteadExemption` off the assessed value.
6. **Estimated tax** = (assessed − homestead) × `Millage` ÷ 1,000.

Each step is kept as a trace line with the rule names it used. On the parcel map, clicking a parcel shows this "why this value" trace, and each step links back to its rule lines in the editor. `Valuer.ValueAll` values every parcel.

## The ratio study
`RatioStudy` measures assessment quality over the parcels that have a sale, using each one's ratio of estimated market value to sale price. It reports three IAAO statistics:

| Statistic | How it's worked out | Target |
|---|---|---|
| **Median ratio** | The middle ratio (the mean of the two middle ones for an even count). Measures the level of assessment. | 0.95 to 1.05 |
| **COD** (coefficient of dispersion) | The average absolute difference from the median, as a percentage of the median. Measures uniformity. | 15 or less |
| **PRD** (price-related differential) | The mean ratio divided by the value-weighted mean ratio (total market value ÷ total sale price). Above 1 means cheaper homes are assessed higher, relative to price, than dearer ones. | 0.98 to 1.03 |

**Compare runs** shows these before and after a rules change, overall and for each neighborhood, against the targets, with a histogram of ratios.

## Sample data
Parcels and sales are made up (`SampleData.cs`), and neighborhood N-04 starts out under-assessed so a rules change has something to fix. Parcel shapes are building footprints from OpenStreetMap around Forest Park Cemetery in Shreveport, not real lot lines. The millage of 120 is a sample rate; the 10% assessment ratio and the $7,500 homestead exemption follow Louisiana's residential rules.
