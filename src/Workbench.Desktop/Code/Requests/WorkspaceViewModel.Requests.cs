namespace Workbench.Desktop;

// Requests page (everyone in a company): report a bug or ask for a feature, and follow it from triage through GitLab to the fix.
public sealed partial class WorkspaceViewModel
{
    public RecordPage<IssueReport> Requests { get; }

    // Where the person was in Levy when they pressed Report an issue (page and selected parcel); set by the main window.
    public Func<string>? ContextSource { get; set; }

    // "Goes to your account manager, …" under the request form.
    public string RequestRouting => _membership.AccountManagerName.Length > 0
        ? $"Goes to your account manager, {_membership.AccountManagerName}, who sends it on to the developers."
        : "Goes to your account manager, who sends it on to the developers.";

    RecordPage<IssueReport> CreateRequests() => new(this, new PageRules<IssueReport>(
        Noun: "request",
        NewText: "Report an issue",
        MineLabel: "Filed by me",
        FilterValues: Choices.RequestStatuses,
        AllLabel: "All requests",
        SearchText: r => $"{r.Title} {r.Kind} {r.ReporterName} {r.Description} {r.Context}",
        FilterValue: r => r.Status,
        Mine: r => r.ReportedBy,
        Create: () => new IssueReport { Context = ContextSource?.Invoke() ?? "" },
        Validate: r => string.IsNullOrWhiteSpace(r.Title) ? "Give the request a short title." : null,
        Counters: rows =>
        [
            Counter("Open", rows.Count(r => r.IsOpen), "filed and not finished"),
            Counter("Waiting for triage", rows.Count(r => r.Status == "New"), "with your account manager"),
            Counter("Being worked", rows.Count(r => r.Status is "In GitLab" or "In progress" or "In review"), "with the developers"),
            Counter("Fixed", rows.Count(r => r.Status == "Fixed"), "delivered"),
        ]), FileRequestAsync, _me);

    // Filed requests are the account manager's to change; the reporter can only file new ones.
    async Task<IssueReport> FileRequestAsync(IssueReport draft, bool isNew)
    {
        if (!isNew) throw new SupabaseException("Your account manager updates a request once it's filed. Add details by filing a follow-up.");
        draft.OrgId = OrganizationId;
        return await _client.InsertAsync<IssueReport>("issue_reports", draft.Columns());
    }

    public Task LoadRequestsAsync() => LoadInto(Requests, () => _client.GetAsync<IssueReport>("issue_reports", $"select=*&org_id=eq.{OrganizationId}&order=created_at.desc"));

    async Task LoadInto<T>(RecordPage<T> page, Func<Task<List<T>>> read) where T : Row
    {
        try { page.Load(await read()); }
        catch (SupabaseException e) { AdminMessage = e.Message; }
    }
}
