using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace Workbench.Desktop;

// One issue in the Mock GitLab list, ranked by its priority label.
public sealed record MockIssueRow(MockIssue Issue, int Rank, IReadOnlyList<Chip> Labels, Chip State, string Meta)
{
    public string Heading => $"#{Issue.Iid} {Issue.Title}";
}

// The Mock GitLab window: issues by priority, the selected issue, its merge request, and the developer and reviewer actions,
// done as the person chosen in "Acting as".
public sealed class MockGitLabViewModel : INotifyPropertyChanged
{
    readonly MockGitLab _gitLab;
    string _project;
    bool _showClosed;
    MockIssueRow? _selected;
    string _actingAs;
    bool _failNextPipeline;
    string _message = "";
    bool _busy;
    bool _rebuilding;

    public MockGitLabViewModel(MockGitLab gitLab, string project, int? issue = null)
    {
        _gitLab = gitLab;
        _project = project;
        _actingAs = gitLab.People.FirstOrDefault() ?? "Developer";
        gitLab.Changed += Rebuild;
        Rebuild();
        if (issue is int iid) Select(project, iid);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    // Stops following the demo GitLab when the window closes.
    public void Detach() => _gitLab.Changed -= Rebuild;

    public string Host => _gitLab.Host;
    public IReadOnlyList<string> Projects => _gitLab.Projects.Select(p => p.Path).ToList();
    public IReadOnlyList<string> People => _gitLab.People;
    public string Project { get => _project; set { if (value is null || value == _project) return; _project = value; Raise(); _selected = null; Rebuild(); } }
    public string ActingAs { get => _actingAs; set { _actingAs = value; Raise(); Message = ""; } }
    public bool ShowOpen { get => !_showClosed; set { if (value) SetShowClosed(false); } }
    public bool ShowClosed { get => _showClosed; set { if (value) SetShowClosed(true); } }
    public bool FailNextPipeline { get => _failNextPipeline; set { _failNextPipeline = value; Raise(); } }
    public string Message { get => _message; private set { _message = value; Raise(); Raise(nameof(HasMessage)); } }
    public bool HasMessage => _message.Length > 0;
    public string Counts { get; private set; } = "";

    public IReadOnlyList<MockIssueRow> Issues { get; private set; } = [];
    // The list clears its selection while its rows are replaced; that clearing is ignored so the open issue stays open.
    public MockIssueRow? Selected { get => _selected; set { if (_rebuilding || ReferenceEquals(value, _selected)) return; _selected = value; Message = ""; RaiseDetail(); } }

    // The selected issue.
    public bool HasIssue => _selected is not null;
    public MockIssue? Issue => _selected?.Issue;
    // The description without markdown bold and code marks, which real GitLab would render.
    public string Description => _selected?.Issue.Description.Replace("**", "").Replace("`", "") ?? "";
    public string IssueRef => _selected is null ? "" : $"{_project}#{_selected.Issue.Iid}";
    public string Assignee => _selected?.Issue.Assignee ?? "Unassigned";
    public bool IssueOpen => _selected?.Issue.State == "opened";
    public bool IsDoing => _selected?.Issue.Labels.Contains("workflow::doing") == true;
    public string Opened => _selected is null ? "" : $"Opened {_selected.Issue.CreatedAt:MMM d} by {_selected.Issue.Author}";

    // Its merge request: the open one, or the latest.
    public MockMergeRequest? MergeRequest => _selected is null ? null
        : _gitLab.Projects.First(p => p.Path == _project).MergeRequests.Where(m => m.ClosesIssue == _selected.Issue.Iid)
            .OrderByDescending(m => m.State == "opened").ThenByDescending(m => m.Iid).FirstOrDefault();
    public bool HasMergeRequest => MergeRequest is not null;
    public bool CanOpenMergeRequest => IssueOpen && MergeRequest?.State != "opened";
    public bool MergeRequestOpen => MergeRequest?.State == "opened";
    public string MergeRequestRef => MergeRequest is { } m ? $"!{m.Iid} · {m.SourceBranch} → main · by {m.Author}" : "";
    public Chip MergeRequestState => MergeRequest is { } m
        ? new(m.State == "opened" && m.Draft ? "Draft" : char.ToUpperInvariant(m.State[0]) + m.State[1..], m.State switch { "merged" => "Purple", "closed" => "Red", _ => m.Draft ? "Grey" : "Green" })
        : new("", "Grey");
    public Chip PipelineState => MergeRequest?.Pipeline switch
    {
        "success" => new("Pipeline passed", "Green"),
        "failed" => new("Pipeline failed", "Red"),
        "running" => new("Pipeline running…", "Blue"),
        _ => new("No pipeline yet", "Grey"),
    };
    public string Approvals => MergeRequest is { } m
        ? $"{m.ApprovedBy.Count} of {_gitLab.RequiredApprovals} required approval{(_gitLab.RequiredApprovals == 1 ? "" : "s")}{(m.ApprovedBy.Count > 0 ? ": " + string.Join(", ", m.ApprovedBy) : "")}"
        : "";
    public string MergeCheck => MergeRequest is { } m ? _gitLab.WhyNotMergeable(m) ?? "Ready to merge." : "";
    public bool Mergeable => MergeRequest is { } m && _gitLab.WhyNotMergeable(m) is null;

    public ICommand AssignToMeCommand => Do(() => _gitLab.Assign(_project, Issue!.Iid, _actingAs), () => $"Assigned to {_actingAs}.");
    public ICommand StartWorkCommand => Do(() => _gitLab.StartWork(_project, Issue!.Iid), () => "Labelled workflow::doing.");
    public ICommand CloseIssueCommand => Do(() => _gitLab.CloseIssue(_project, Issue!.Iid), () => "Issue closed without a merge.");
    public ICommand ReopenIssueCommand => Do(() => _gitLab.ReopenIssue(_project, Issue!.Iid), () => "Issue reopened.");
    public ICommand OpenMergeRequestCommand => Do(() => _gitLab.OpenMergeRequest(_project, Issue!.Iid, _actingAs), () => "Draft merge request opened.");
    public ICommand MarkReadyCommand => Do(() => _gitLab.MarkReady(_project, MergeRequest!.Iid), () => "Marked ready for review.");
    public ICommand ApproveCommand => Do(() => _gitLab.Approve(_project, MergeRequest!.Iid, _actingAs), () => $"Approved by {_actingAs}.");
    public ICommand MergeCommand => Do(() => _gitLab.Merge(_project, MergeRequest!.Iid), () => "Merged. The issue closed with it.");
    public ICommand CloseMergeRequestCommand => Do(() => _gitLab.CloseMergeRequest(_project, MergeRequest!.Iid), () => "Merge request closed.");
    public ICommand RunPipelineCommand => new RelayCommand(() => _ = RunPipelineAsync());
    public ICommand ResetCommand => new RelayCommand(() => { _gitLab.Reset(); _selected = null; Rebuild(); Message = "The demo GitLab was reset to its starting issues."; });

    // Opens the window on a particular issue (from a link on Levy's Intake page).
    public void Select(string project, int iid)
    {
        if (Projects.Contains(project)) { _project = project; Raise(nameof(Project)); }
        var issue = _gitLab.Projects.First(p => p.Path == _project).Issues.FirstOrDefault(i => i.Iid == iid);
        if (issue is not null && (issue.State == "opened") == _showClosed) { _showClosed = !_showClosed; Raise(nameof(ShowOpen)); Raise(nameof(ShowClosed)); }
        Rebuild();
        Selected = Issues.FirstOrDefault(r => r.Issue.Iid == iid);
    }

    // A push: the pipeline runs for a moment, then passes (or fails, when "Next pipeline fails" is ticked).
    async Task RunPipelineAsync()
    {
        if (_busy || MergeRequest is not { } mr) return;
        if (_gitLab.StartPipeline(_project, mr.Iid) is { } problem) { Message = problem; return; }
        _busy = true;
        Message = "Pipeline running: build, tests, lint…";
        await Task.Delay(TimeSpan.FromSeconds(2.5));
        var passed = !_failNextPipeline;
        _gitLab.FinishPipeline(_project, mr.Iid, passed);
        FailNextPipeline = false;
        _busy = false;
        Message = passed ? "Pipeline passed." : "Pipeline failed: 2 tests failed. Push a fix and run it again.";
    }

    // Runs an action; the done text is worked out after it runs, so it names whoever is acting now.
    ICommand Do(Func<string?> action, Func<string> done) => new RelayCommand(() =>
    {
        if (_selected is null) return;
        Message = action() ?? done();
    });

    void SetShowClosed(bool closed)
    {
        if (_showClosed == closed) return;
        _showClosed = closed;
        _selected = null;
        Raise(nameof(ShowOpen)); Raise(nameof(ShowClosed));
        Rebuild();
    }

    // Rebuilds the list (priority first, then oldest) and keeps the selected issue selected.
    void Rebuild()
    {
        var project = _gitLab.Projects.First(p => p.Path == _project);
        var keep = _selected?.Issue.Iid;
        Issues = project.Issues.Where(i => (i.State == "opened") != _showClosed)
            .Select(i => new MockIssueRow(i, Rank(i), i.Labels.Select(LabelChip).ToList(), new(i.State == "opened" ? "Open" : "Closed", i.State == "opened" ? "Green" : "Grey"),
                i.Assignee is null ? "Unassigned" : "Assigned to " + i.Assignee))
            .OrderBy(r => r.Rank).ThenBy(r => r.Issue.Iid).ToList();
        Counts = $"{project.Issues.Count(i => i.State == "opened")} open · {project.Issues.Count(i => i.State != "opened")} closed";
        _selected = keep is int iid ? Issues.FirstOrDefault(r => r.Issue.Iid == iid) : null;
        _rebuilding = true;
        Raise(nameof(Issues)); Raise(nameof(Counts)); Raise(nameof(Projects));
        _rebuilding = false;
        RaiseDetail();
    }

    void RaiseDetail()
    {
        foreach (var name in new[] { nameof(Selected), nameof(HasIssue), nameof(Issue), nameof(Description), nameof(IssueRef), nameof(Assignee), nameof(IssueOpen), nameof(IsDoing), nameof(Opened),
                     nameof(MergeRequest), nameof(HasMergeRequest), nameof(CanOpenMergeRequest), nameof(MergeRequestOpen), nameof(MergeRequestRef), nameof(MergeRequestState),
                     nameof(PipelineState), nameof(Approvals), nameof(MergeCheck), nameof(Mergeable) })
            Raise(name);
    }

    static int Rank(MockIssue issue) => issue.Labels.FirstOrDefault(l => l.StartsWith("priority::")) switch
    {
        "priority::critical" => 0, "priority::high" => 1, "priority::medium" => 2, "priority::low" => 3, _ => 4,
    };

    // Scoped labels coloured the way a team would set them up.
    static Chip LabelChip(string label) => new(label, label switch
    {
        "type::bug" or "priority::critical" or "priority::high" => "Red",
        "type::feature" => "Blue",
        "priority::medium" => "Amber",
        "workflow::doing" => "Green",
        _ when label.StartsWith("stack::") => "Purple",
        _ => "Grey",
    });

    void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
