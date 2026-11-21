using System.Globalization;
using System.Windows.Input;

namespace Workbench.Desktop;

// Intake page (account managers and platform admins): requests from the companies they manage. Triage them, send them to the
// right GitLab project with labels, and pull back what GitLab says (issue state, merge requests, pipelines).
public sealed partial class WorkspaceViewModel
{
    readonly GitLabSettings _gitLab = GitLabSettings.FromAssets();
    string _intakeMessage = "";
    bool _intakeFailed;
    bool _gitLabBusy;

    public RecordPage<IssueReport> Intake { get; }

    public bool IsAccountManager => _membership.IsAccountManager || IsPlatformAdmin;
    public string IntakeMessage { get => _intakeMessage; private set { _intakeMessage = value; Raise(); Raise(nameof(HasIntakeMessage)); } }
    public bool IntakeFailed { get => _intakeFailed; private set { _intakeFailed = value; Raise(); } }
    public bool HasIntakeMessage => _intakeMessage.Length > 0;
    public bool GitLabBusy { get => _gitLabBusy; private set { _gitLabBusy = value; Raise(); Raise(nameof(IsGitLabIdle)); } }
    public bool IsGitLabIdle => !_gitLabBusy;

    // "Connected to GitLab as @user" or why not.
    public string GitLabStatus => _client.HasGitLab
        ? $"Connected to {new Uri(_gitLab.Url).Host}{(_client.User?.GitLabUsername is { } user ? " as @" + user : "")}"
        : $"Not connected to {new Uri(_gitLab.Url).Host}";
    public bool NeedsGitLab => !_client.HasGitLab;

    public ICommand SendToGitLabCommand => new RelayCommand(() => _ = SendToGitLabAsync());
    public ICommand RefreshGitLabCommand => new RelayCommand(() => _ = RefreshFromGitLabAsync());
    public ICommand ConnectGitLabCommand => new RelayCommand(() => _ = ConnectGitLabAsync());

    RecordPage<IssueReport> CreateIntake() => new(this, new PageRules<IssueReport>(
        Noun: "request",
        ReadOnly: true,
        FilterValues: Choices.RequestStatuses,
        AllLabel: "All requests",
        SearchText: r => $"{r.Title} {r.OrgName} {r.ReporterName} {r.Kind} {r.Description} {r.Where}",
        FilterValue: r => r.Status,
        Mine: null,
        Create: () => new IssueReport(),
        Validate: r => string.IsNullOrWhiteSpace(r.Title) ? "Give the request a title." : null,
        Counters: rows =>
        [
            Counter("To triage", rows.Count(r => r.Status == "New"), "new from clients"),
            Counter("Ready for GitLab", rows.Count(r => r.Status == "Triaged" && r.CanSend), "triaged, not sent"),
            Counter("In GitLab", rows.Count(r => r.Status is "In GitLab" or "In progress" or "In review"), "open with developers"),
            Counter("In review", rows.Count(r => r.Status == "In review"), "merge request open"),
        ]), SaveTriageAsync, _me);

    async Task<IssueReport> SaveTriageAsync(IssueReport draft, bool isNew)
    {
        var stored = await _client.UpdateAsync<IssueReport>("issue_reports", draft.Id, draft.TriageColumns());
        stored.OrgName = draft.OrgName;
        return stored;
    }

    // Reads every request this person triages, then brings GitLab's side up to date.
    public async Task LoadIntakeAsync()
    {
        if (!IsAccountManager) return;
        try
        {
            var rows = await _client.RpcAsync<IssueReport>("list_intake");
            Intake.Load(rows);
        }
        catch (SupabaseException e) { ShowIntake(e.Message, failed: true); return; }
        if (_client.HasGitLab) await RefreshFromGitLabAsync();
    }

    // Opens the issue in the project for the request's codebase, labelled for triage boards, and records where it went.
    async Task SendToGitLabAsync()
    {
        if (Intake.Draft is not { CanSend: true } draft || _gitLabBusy) return;
        if (draft.Stack is null || draft.Priority is null) { ShowIntake("Set the codebase and priority first; they decide the GitLab project and labels.", failed: true); return; }
        if (!_gitLab.Projects.TryGetValue(draft.Stack, out var project)) { ShowIntake($"No GitLab project is set for {draft.Stack} in Assets/Account/gitlab.json.", failed: true); return; }
        if (!_client.HasGitLab) { ShowIntake("Connect GitLab first, then send the request.", failed: true); return; }
        GitLabBusy = true;
        try
        {
            var issue = await new GitLabClient(_gitLab, _client.GitLab!).CreateIssueAsync(project, draft.Title.Trim(), IssueBody(draft), Labels(draft));
            draft.GitlabProject = project;
            draft.GitlabIid = issue.Iid;
            draft.GitlabUrl = issue.WebUrl;
            draft.GitlabState = issue.State;
            draft.Status = "In GitLab";
            var stored = await _client.UpdateAsync<IssueReport>("issue_reports", draft.Id, draft.GitLabColumns());
            stored.OrgName = draft.OrgName;
            Intake.Replace(stored);
            Intake.Refresh();
            Intake.Open(stored);
            ShowIntake($"Sent to GitLab as {project}#{issue.Iid}.", failed: false);
        }
        catch (SupabaseException e) { ShowIntake(e.Message, failed: true); }
        finally { GitLabBusy = false; }
    }

    // Asks GitLab about every open request that has an issue, and saves what changed so clients see it on their Requests page.
    async Task RefreshFromGitLabAsync()
    {
        if (_gitLabBusy) return;
        if (!_client.HasGitLab) { ShowIntake("Connect GitLab to refresh issue and merge request status.", failed: true); return; }
        GitLabBusy = true;
        var gitLab = new GitLabClient(_gitLab, _client.GitLab!);
        var changed = 0;
        try
        {
            foreach (var request in Intake.Items.Where(r => r.IsInGitLab && r.Status is not ("Closed" or "Declined")).ToList())
            {
                var issue = await gitLab.GetIssueAsync(request.GitlabProject!, request.GitlabIid!.Value);
                var mergeRequests = (await gitLab.MergeRequestsForAsync(request.GitlabProject!, request.GitlabIid.Value))
                    .Select(m => new MergeRequestInfo(m.Iid, m.Title, m.State, m.WebUrl, m.Draft, m.Pipeline)).ToList();
                var status = StatusFrom(issue, mergeRequests);
                if (status == request.Status && issue.State == request.GitlabState && mergeRequests.SequenceEqual(request.MergeRequests)) continue;
                var stored = await _client.UpdateAsync<IssueReport>("issue_reports", request.Id, new { Status = status, GitlabState = issue.State, MergeRequests = mergeRequests });
                stored.OrgName = request.OrgName;
                Intake.Replace(stored);
                changed++;
            }
            Intake.Refresh();
            ShowIntake($"Checked GitLab at {DateTime.Now.ToString("h:mm tt", CultureInfo.GetCultureInfo("en-US"))}: {(changed == 0 ? "no changes" : changed == 1 ? "1 request updated" : $"{changed} requests updated")}.", failed: false);
        }
        catch (SupabaseException e) { ShowIntake(e.Message, failed: true); }
        finally { GitLabBusy = false; }
    }

    // Signs in with GitLab again for a fresh token; it must be the same Levy account.
    async Task ConnectGitLabAsync()
    {
        if (_gitLabBusy) return;
        GitLabBusy = true;
        ShowIntake("Finish signing in to GitLab in your browser.", failed: false);
        try
        {
            using var wait = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            await _client.SignInWithGitLabAsync(_gitLab, wait.Token, onlyFor: _client.User?.Id);
            Raise(nameof(GitLabStatus)); Raise(nameof(NeedsGitLab));
            ShowIntake("Connected to GitLab.", failed: false);
        }
        catch (OperationCanceledException) { ShowIntake("GitLab sign-in timed out.", failed: true); }
        catch (SupabaseException e) { ShowIntake(e.Message, failed: true); }
        finally { GitLabBusy = false; }
        if (_client.HasGitLab) await RefreshFromGitLabAsync();
    }

    // Closed with a merged merge request is fixed; an open, non-draft merge request is in review; a draft, an assignee or a
    // "doing" / "in progress" label is in progress; otherwise it's waiting in GitLab.
    static string StatusFrom(GitLabIssue issue, IReadOnlyList<MergeRequestInfo> mergeRequests) =>
        issue.State == "closed" ? (mergeRequests.Any(m => m.State == "merged") ? "Fixed" : "Closed")
        : mergeRequests.Any(m => m.State == "opened" && !m.Draft) ? "In review"
        : mergeRequests.Any(m => m.State == "opened") || issue.Assignees > 0
          || issue.Labels.Any(l => l.Contains("doing", StringComparison.OrdinalIgnoreCase) || l.Contains("in progress", StringComparison.OrdinalIgnoreCase)) ? "In progress"
        : "In GitLab";

    // Scoped labels drive GitLab issue boards: developers pull by priority, filter by client or codebase.
    static IEnumerable<string> Labels(IssueReport r) =>
        [$"type::{r.Kind.ToLowerInvariant()}", $"priority::{r.Priority!.ToLowerInvariant()}", $"stack::{r.Stack!.ToLowerInvariant()}", $"client::{r.OrgName}", "source::levy"];

    static string IssueBody(IssueReport r) => string.Join("\n", new[]
    {
        r.Description.Trim().Length > 0 ? r.Description.Trim() : "_No description given._",
        "",
        "---",
        $"**Requested by:** {r.ReporterName} ({r.OrgName}), {r.Filed}",
        r.Context.Trim().Length > 0 ? $"**Where in Levy:** {r.Context.Trim()}" : null,
        r.TriageNote.Trim().Length > 0 ? $"**Account manager's note:** {r.TriageNote.Trim()}" : null,
        $"**Levy request:** `{r.Id}`",
    }.Where(line => line is not null));

    void ShowIntake(string message, bool failed)
    {
        IntakeFailed = failed;
        IntakeMessage = message;
        Raise(nameof(GitLabStatus)); Raise(nameof(NeedsGitLab));
    }
}
