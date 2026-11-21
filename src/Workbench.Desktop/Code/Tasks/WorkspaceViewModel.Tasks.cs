namespace Workbench.Desktop;

// Tasks page: the work items the scrum board also shows, each optionally under a project or a job.
public sealed partial class WorkspaceViewModel
{
    public RecordPage<WorkTask> Tasks { get; }

    RecordPage<WorkTask> CreateTasks() => new(this, new PageRules<WorkTask>(
        Noun: "task",
        FilterValues: Choices.TaskStatuses,
        AllLabel: "All statuses",
        SearchText: t => $"{t.Title} {t.ProjectName} {t.JobTitle} {t.AssigneeName}",
        FilterValue: t => t.Status,
        Mine: t => t.AssigneeId,
        Create: () => new WorkTask { Status = "To do", AssigneeId = _me, DueDate = Today.AddDays(7), Points = 1, SortOrder = NextSortOrder("To do") },
        Validate: t => string.IsNullOrWhiteSpace(t.Title) ? "Give the task a title." : t.Points < 0 ? "Points can't be negative." : null,
        Counters: rows =>
        [
            Counter("Open", rows.Count(t => t.IsOpen), $"{rows.Where(t => t.IsOpen).Sum(t => t.Points)} points"),
            Counter("Due this week", rows.Count(t => t.IsOpen && t.DueDate >= Today && t.DueDate <= Today.AddDays(7)), "next 7 days"),
            Counter("Overdue", rows.Count(t => t.IsOverdue), "past due"),
            Counter("High priority", rows.Count(t => t.IsOpen && t.Priority == "High"), "still open"),
        ]), (draft, isNew) => SaveAsync("tasks", draft, isNew), _me);

    // Puts a task at the bottom of its board column.
    int NextSortOrder(string status) => Tasks.Items.Where(t => t.Status == status).Select(t => t.SortOrder + 1).DefaultIfEmpty(0).Max();
}
