namespace Workbench.Desktop;

// Projects page: longer pieces of work (a reappraisal, a model refresh) that tasks roll up to.
public sealed partial class WorkspaceViewModel
{
    public RecordPage<Project> Projects { get; }

    RecordPage<Project> CreateProjects() => new(this, new PageRules<Project>(
        Noun: "project",
        FilterValues: Choices.ProjectStatuses,
        AllLabel: "All statuses",
        SearchText: p => $"{p.Name} {p.Description} {p.OwnerName}",
        FilterValue: p => p.Status,
        Mine: p => p.OwnerId,
        Create: () => new Project { OwnerId = _me, StartDate = Today, DueDate = Today.AddDays(60) },
        Validate: p => Required(p.Name, "project") ?? (p.Progress is < 0 or > 100 ? "Progress is a percentage from 0 to 100." : null)
            ?? (p.StartDate > p.DueDate ? "The due date is before the start date." : null),
        Counters: rows =>
        {
            var open = rows.Where(p => p.Status == "Open").ToList();
            return
            [
                Counter("Open", open.Count, "under way"),
                Counter("On hold", rows.Count(p => p.Status == "On hold"), "paused"),
                Counter("Due in 30 days", rows.Count(p => p.Status != "Closed" && p.DueDate >= Today && p.DueDate <= Today.AddDays(30)), "coming up"),
                new SummaryCard("Average progress", open.Count == 0 ? "—" : $"{open.Average(p => p.Progress):0}%", "across open projects", "", false, false),
            ];
        }), (draft, isNew) => SaveAsync("projects", draft, isNew), _me);
}
