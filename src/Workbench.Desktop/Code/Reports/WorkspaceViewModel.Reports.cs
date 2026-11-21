namespace Workbench.Desktop;

// Reports page: ratio studies, appeal decisions and roll summaries, from draft to closed.
public sealed partial class WorkspaceViewModel
{
    public RecordPage<Report> Reports { get; }

    RecordPage<Report> CreateReports() => new(this, new PageRules<Report>(
        Noun: "report",
        FilterValues: Choices.ReportStatuses,
        AllLabel: "All statuses",
        SearchText: r => $"{r.Title} {r.Kind} {r.AuthorName} {r.Summary}",
        FilterValue: r => r.Status,
        Mine: r => r.AuthorId,
        Create: () => new Report { AuthorId = _me },
        Validate: r => string.IsNullOrWhiteSpace(r.Title) ? "Give the report a title." : null,
        Counters: rows =>
        [
            Counter("Drafts", rows.Count(r => r.Status == "Draft"), "being written"),
            Counter("In review", rows.Count(r => r.Status == "In review"), "awaiting sign-off"),
            Counter("Closed this month", rows.Count(r => r.ClosedOn is { } d && d.Year == Today.Year && d.Month == Today.Month), Today.ToString("MMMM")),
            Counter("Closed", rows.Count(r => r.Status == "Closed"), "all time"),
        ]), (draft, isNew) => SaveAsync("reports", draft, isNew), _me);
}
