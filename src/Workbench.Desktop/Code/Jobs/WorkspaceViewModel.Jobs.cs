namespace Workbench.Desktop;

// Jobs page: appeals, reassessments and reviews, each for a client with a due date and hours.
public sealed partial class WorkspaceViewModel
{
    public RecordPage<Job> Jobs { get; }

    RecordPage<Job> CreateJobs() => new(this, new PageRules<Job>(
        Noun: "job",
        FilterValues: Choices.JobStatuses,
        AllLabel: "All statuses",
        SearchText: j => $"{j.Title} {j.ClientName} {j.JobType} {j.AssigneeName}",
        FilterValue: j => j.Status,
        Mine: j => j.AssigneeId,
        Create: () => new Job { AssigneeId = _me, DueDate = Today.AddDays(14) },
        Validate: j => string.IsNullOrWhiteSpace(j.Title) ? "Give the job a title." : j.BudgetHours < 0 || j.ActualHours < 0 ? "Hours can't be negative." : null,
        Counters: rows =>
        [
            Counter("Due this week", rows.Count(j => j.IsOpen && j.DueDate >= Today && j.DueDate <= Today.AddDays(7)), "next 7 days"),
            Counter("Overdue", rows.Count(j => j.IsOverdue), "past due"),
            Counter("In progress", rows.Count(j => j.Status == "In progress"), $"{rows.Where(j => j.IsOpen).Sum(j => j.ActualHours):0.#} h logged on open jobs"),
            Counter("Waiting on client", rows.Count(j => j.Status == "Waiting on client"), "need a reply"),
        ]), (draft, isNew) => SaveAsync("jobs", draft, isNew), _me);
}
