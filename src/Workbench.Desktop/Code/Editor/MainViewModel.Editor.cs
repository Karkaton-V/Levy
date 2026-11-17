using Workbench.Engine;
using static Workbench.Desktop.Shared;

namespace Workbench.Desktop;

// One entry in the problems list: the rules line it points at (0 when it has none) and what is wrong.
public sealed record ProblemRow(int Line, string Where, string Message);

// One neighborhood line under the ratio study.
public sealed record HoodRow(string Name, string Sales, string Median, string Verdict, bool IsPass);

// The Editor tab's part of the view model: problems, per-neighborhood results and Ctrl+Space rule names.
public sealed partial class MainViewModel
{
    List<(string? Key, string Message)> _modelProblems = new();

    public IReadOnlyList<ProblemRow> Problems { get; private set; } = Array.Empty<ProblemRow>();
    public string ProblemsSummary { get; private set; } = "";
    public IReadOnlyList<HoodRow> Hoods { get; private set; } = Array.Empty<HoodRow>();

    // Rule names offered by Ctrl+Space in the editor: one LandRate per neighborhood, then the fixed rules.
    public IReadOnlyList<(string Key, string Description)> RuleCompletions =>
        _parcels.Select(p => "LandRate." + p.Neighborhood).Distinct().Order().Select(k => (k, DescribeRule(k) ?? ""))
            .Concat(RuleSet.Known.Skip(1).Select(k => (k.Key, k.Description))).ToList();

    // After a run: neighborhood results, and problems that come from the values rather than from typos.
    void UpdateEditorAfterRun(List<Diagnostic> diagnostics)
    {
        _modelProblems = diagnostics.Where(d => d.Line == 0).Select(d => ((string?)null, d.Message)).ToList();
        var hoods = new List<HoodRow>();
        foreach (var group in _current.GroupBy(v => v.Parcel.Neighborhood).OrderBy(g => g.Key))
        {
            var s = RatioStudy.Compute(group);
            var ok = s.Count == 0 || RatioStudy.MedianOk(s.Median);
            hoods.Add(new HoodRow(group.Key, $"{s.Count} sales", s.Count == 0 ? "no sales" : $"median {Number(s.Median, "F3")}", ok ? "Within target" : "Outside target", ok));
            if (!ok) _modelProblems.Add(("LandRate." + group.Key, $"{group.Key} median ratio is {Number(s.Median, "F3")}, outside 0.95 to 1.05. Adjust this land rate."));
        }
        Hoods = hoods;
        RefreshProblems();
    }

    // Typos come from parsing the current text; model problems come from the last run, pointed at their rule's current line.
    void RefreshProblems()
    {
        RuleSet.Parse(_rulesText, out var parse);
        var lines = RuleLines(_rulesText);
        var rows = parse.Select(d => new ProblemRow(d.Line, $"Ln {d.Line}", d.Message)).ToList();
        foreach (var (key, message) in _modelProblems)
        {
            var line = key is not null && lines.TryGetValue(key, out var found) ? found : 0;
            rows.Add(new ProblemRow(line, line > 0 ? $"Ln {line}" : "Model", message));
        }
        Problems = rows;
        ProblemsSummary = rows.Count == 0 ? "No problems" : rows.Count == 1 ? "1 problem" : $"{rows.Count} problems";
        Raise(nameof(Problems)); Raise(nameof(ProblemsSummary));
    }
}
