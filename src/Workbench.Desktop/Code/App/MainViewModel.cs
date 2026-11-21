using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Workbench.Engine;
using static Workbench.Desktop.Shared;

namespace Workbench.Desktop;

// A ratio-study number with its target and pass/fail wording; shown on the Editor and Compare tabs.
public sealed record StatTile(string Name, string Value, string Before, string Target, string Status, bool IsPass);

// One card in the summary strip under the top bar; HasStatus shows a pass/fail pill beside the detail.
public sealed record SummaryCard(string Label, string Value, string Detail, string Status, bool HasStatus, bool IsPass);

// State all three tabs share: the rules text and each run's valuations. Each tab adds its own part in Code/<Tab>/MainViewModel.<Tab>.cs.
public sealed partial class MainViewModel : INotifyPropertyChanged
{
    readonly StudyArea _area;
    IReadOnlyList<Parcel> _parcels = SampleData.Create();
    List<Valuation> _current = new();
    List<Valuation>? _previous;
    int _runNumber;
    string _rulesText;
    string _lastRunText = "";

    // Starts from the given rules text (normally Assets/Editor/DefaultRules.rules) and the study area to load (Assets/Map/study-area.json).
    public MainViewModel(string rulesText, StudyArea area)
    {
        _rulesText = rulesText;
        _area = area;
        RunCommand = new RelayCommand(Run);
        OpenInEditorCommand = new ParameterCommand(line => OpenInEditorRequested?.Invoke(line is int n && n > 0 ? n : 1));
        ZoomToSelectedCommand = new RelayCommand(() => ZoomToSelectionRequested?.Invoke());
        Run();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    // Raised with a line number when the user asks to see that line in the Editor tab.
    public event Action<int>? OpenInEditorRequested;

    public ICommand RunCommand { get; }
    public ICommand OpenInEditorCommand { get; }

    // Setting the text re-checks it straight away for every tab; values only change when the model is run.
    public string RulesText
    {
        get => _rulesText;
        set
        {
            if (_rulesText == value) return;
            _rulesText = value;
            Raise();
            RefreshProblems();
            RefreshRulesInWords();
            RefreshTrace();
            Raise(nameof(IsStale));
        }
    }

    public bool IsStale => _rulesText != _lastRunText;
    public string StatusLine { get; private set; } = "";
    public IReadOnlyList<StatTile> Tiles { get; private set; } = Array.Empty<StatTile>();
    public IReadOnlyList<SummaryCard> Summary { get; private set; } = Array.Empty<SummaryCard>();
    public string Subtitle { get; private set; } = "";

    bool _isWorkflowPage;
    string _workspaceName = "";

    // True on the workflow pages (Jobs, Projects, ...), which hide the summary strip and Run and show the organization as the subtitle.
    public bool IsWorkflowPage
    {
        get => _isWorkflowPage;
        set { if (_isWorkflowPage == value) return; _isWorkflowPage = value; Raise(); Raise(nameof(IsValuationPage)); Raise(nameof(PageSubtitle)); }
    }

    public bool IsValuationPage => !_isWorkflowPage;
    public string WorkspaceName { get => _workspaceName; set { _workspaceName = value; Raise(); Raise(nameof(PageSubtitle)); } }
    public string PageSubtitle => _isWorkflowPage ? _workspaceName : Subtitle;

    // Parses the rules, revalues every parcel, then lets each tab rebuild what it shows.
    void Run()
    {
        var rules = RuleSet.Parse(RulesText, out var diagnostics);
        var selectedIndex = RememberSelection();
        if (_runNumber > 0) _previous = _current;
        _current = Valuer.ValueAll(_parcels, rules, diagnostics);
        _runNumber++;

        var stats = RatioStudy.Compute(_current);
        var before = _previous is null ? null : RatioStudy.Compute(_previous);
        Tiles = new[]
        {
            Tile("Median ratio", stats.Median, before?.Median, "F3", "0.95–1.05", RatioStudy.MedianOk(stats.Median)),
            Tile("COD (uniformity)", stats.Cod, before?.Cod, "F1", "≤ 15.0", RatioStudy.CodOk(stats.Cod)),
            Tile("PRD (price fairness)", stats.Prd, before?.Prd, "F3", "0.98–1.03", RatioStudy.PrdOk(stats.Prd)),
        };
        Summary = BuildSummary(rules, stats);
        Subtitle = $"{_area.Name} · {rules.ValuationYear} · {Number((double)rules.Millage, "0.##")} mills";
        _lastRunText = _rulesText;

        UpdateEditorAfterRun(diagnostics);
        UpdateMapAfterRun();
        UpdateCompareAfterRun();
        RefreshRulesInWords();
        StatusLine = $"Run {_runNumber} · {_current.Count} valued · {stats.Count} sold";

        RaiseAll();
        RestoreSelection(selectedIndex);
    }

    // Roll totals first, then the three ratio-study measures with their pass/fail pill.
    IReadOnlyList<SummaryCard> BuildSummary(RuleSet rules, RatioStats stats)
    {
        var market = _current.Sum(v => v.MarketValue);
        var tax = _current.Sum(v => v.EstimatedTax);
        string Change(Func<Valuation, decimal> pick)
        {
            if (_previous is null) return "—";
            var delta = _current.Sum(pick) - _previous.Sum(pick);
            return delta == 0 ? "No change" : Signed(delta) + " vs last run";
        }
        var cards = new List<SummaryCard>
        {
            new("Parcels valued", Number(_current.Count, "N0"), $"{stats.Count} sold", "", false, true),
            new("Total market value", Compact(market), Change(v => v.MarketValue), "", false, true),
            new("Estimated tax levy", Compact(tax), Change(v => v.EstimatedTax), "", false, true),
        };
        cards.AddRange(Tiles.Select(t => new SummaryCard(t.Name, t.Value, t.Target, t.IsPass ? "Pass" : "Review", true, t.IsPass)));
        return cards;
    }

    // $24.5M, $812K or $950: short enough for a summary card.
    static string Compact(decimal amount) => amount switch
    {
        >= 1_000_000m => "$" + Number((double)(amount / 1_000_000m), "0.0") + "M",
        >= 10_000m => "$" + Number((double)(amount / 1_000m), "0") + "K",
        _ => Money(amount),
    };

    static StatTile Tile(string name, double value, double? before, string format, string target, bool ok) =>
        new(name, Number(value, format), before is null ? "" : "was " + Number(before.Value, format), target, ok ? "Pass" : "Out of range", ok);

    void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    void RaiseAll() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
}
