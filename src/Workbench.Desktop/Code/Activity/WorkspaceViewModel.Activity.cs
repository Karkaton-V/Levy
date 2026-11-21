namespace Workbench.Desktop;

// Activity page (company admins): everything people did in the company, newest first, from the database's own log.
public sealed partial class WorkspaceViewModel
{
    // How many entries to read at a time; the newest come first.
    const int ActivityLimit = 500;

    bool _loadingActivity;

    public RecordPage<ActivityEntry> Activity { get; }

    RecordPage<ActivityEntry> CreateActivity() => new(this, new PageRules<ActivityEntry>(
        Noun: "event",
        Plural: "activity",
        ReadOnly: true,
        FilterValues: Choices.ActivityKinds,
        AllLabel: "Everything",
        SearchText: a => $"{a.ActorName} {a.Action} {a.Subject} {a.Details}",
        FilterValue: a => a.Entity,
        Mine: null,
        Create: () => new ActivityEntry(),
        Validate: _ => "The activity log can't be edited.",
        Counters: rows =>
        {
            var today = DateTime.Today;
            var week = today.AddDays(-6);
            return
            [
                Counter("Today", rows.Count(a => a.When >= today), "actions logged"),
                Counter("Last 7 days", rows.Count(a => a.When >= week), "actions logged"),
                Counter("Active people", rows.Where(a => a.When >= week).Select(a => a.ActorId).Distinct().Count(), "in the last 7 days"),
                Counter("Team changes", rows.Count(a => a.Entity == "Person" && a.When >= week), "joins, approvals, titles"),
            ];
        }), (_, _) => throw new SupabaseException("The activity log can't be edited."), _me);

    // Reads the latest entries; called when the Activity page opens so it is always current.
    public async Task LoadActivityAsync()
    {
        if (!IsAdmin || _loadingActivity) return;
        _loadingActivity = true;
        try
        {
            Activity.Load(await _client.GetAsync<ActivityEntry>("activity", $"select=*&org_id=eq.{OrganizationId}&order=created_at.desc&limit={ActivityLimit}"));
            AdminMessage = "";
        }
        catch (SupabaseException e) { AdminMessage = e.Message; }
        finally { _loadingActivity = false; }
    }
}
