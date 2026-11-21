using System.Globalization;
using System.Text.Json.Serialization;

namespace Workbench.Desktop;

// A coloured status or priority label for the Chip / ChipText styles; Tone is Blue, Green, Amber, Red, Purple or Grey.
public sealed record Chip(string Text, string Tone);

// One choice in a picker; Guid.Empty stands for "Unassigned" / "None", which a ComboBox can select (null it can't).
public sealed record Option(Guid Id, string Name);

// The values each column allows, matching the check constraints in supabase/migrations.
public static class Choices
{
    public static readonly string[] ClientKinds = ["Individual", "Business", "Trust", "Government"];
    public static readonly string[] JobTypes = ["Appeal", "Reassessment", "Exemption review", "New construction", "Ratio study"];
    public static readonly string[] JobStatuses = ["Not started", "In progress", "Waiting on client", "In review", "Completed"];
    public static readonly string[] ProjectStatuses = ["Open", "On hold", "Closed"];
    public static readonly string[] TaskStatuses = ["Backlog", "To do", "In progress", "In review", "Done"];
    public static readonly string[] Priorities = ["Low", "Medium", "High"];
    public static readonly string[] ReportKinds = ["Ratio study", "Appeal decision", "Roll summary", "Field review"];
    public static readonly string[] ReportStatuses = ["Draft", "In review", "Closed"];
    public static readonly string[] MemberStatuses = ["Pending", "Active", "Rejected", "Disabled"];
    public static readonly string[] MemberRoles = ["Member", "Admin"];
    public static readonly string[] CompanyStates = ["Waiting for review", "No admin", "All set"];
    public static readonly string[] ActivityKinds = ["Job", "Task", "Project", "Client", "Report", "Person", "Request"];
    public static readonly string[] RequestKinds = ["Bug", "Feature", "Question"];
    public static readonly string[] RequestStatuses = ["New", "Triaged", "In GitLab", "In progress", "In review", "Fixed", "Closed", "Declined"];
    public static readonly string[] RequestPriorities = ["Low", "Medium", "High", "Critical"];
    public static readonly string[] Stacks = ["Rails", ".NET"];

    public static Chip Status(string status) => new(status, status switch
    {
        "In progress" or "Open" or "To do" => "Blue",
        "Completed" or "Done" or "Closed" => "Green",
        "Waiting on client" or "On hold" => "Amber",
        "In review" => "Purple",
        _ => "Grey",
    });
}

// Columns every workspace row has. Columns() is what an insert or update sends; display fields are [JsonIgnore] and filled by WorkspaceViewModel.
public abstract class Row
{
    public Guid Id { get; set; }
    public Guid OrgId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public abstract object Columns();

    // A detached copy for the edit form, so Cancel leaves the row as it was.
    public Row Copy() => (Row)MemberwiseClone();

    protected static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);
    protected static string? Blank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    protected static Guid Key(Guid? id) => id ?? Guid.Empty;
    protected static Guid? FromKey(Guid key) => key == Guid.Empty ? null : key;
    // "Nov 21" this year, "Mar 4, 2027" otherwise.
    protected static string Date(DateOnly? date) =>
        date is { } d ? d.ToString(d.Year == Today.Year ? "MMM d" : "MMM d, yyyy", CultureInfo.GetCultureInfo("en-US")) : "—";
}

public sealed class Person
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Name => FullName.Length > 0 ? FullName : Email;
}

public sealed class Client : Row
{
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "Individual";
    public string? ParcelId { get; set; }
    public string? City { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }

    [JsonIgnore] public int OpenJobs { get; set; }
    [JsonIgnore] public Chip KindChip => new(Kind, Kind switch { "Business" => "Blue", "Trust" => "Purple", "Government" => "Amber", _ => "Grey" });

    public override object Columns() => new { OrgId, Name = Name.Trim(), Kind, ParcelId = Blank(ParcelId), City = Blank(City), Email = Blank(Email), Phone = Blank(Phone) };
}

public sealed class Job : Row
{
    public Guid? ClientId { get; set; }
    public string Title { get; set; } = "";
    public string JobType { get; set; } = "Appeal";
    public string Status { get; set; } = "Not started";
    public DateOnly? DueDate { get; set; }
    public Guid? AssigneeId { get; set; }
    public decimal BudgetHours { get; set; }
    public decimal ActualHours { get; set; }

    [JsonIgnore] public string ClientName { get; set; } = "";
    [JsonIgnore] public string AssigneeName { get; set; } = "";
    [JsonIgnore] public Guid ClientKey { get => Key(ClientId); set => ClientId = FromKey(value); }
    [JsonIgnore] public Guid AssigneeKey { get => Key(AssigneeId); set => AssigneeId = FromKey(value); }
    [JsonIgnore] public string Detail => ClientName.Length > 0 ? $"{ClientName} · {JobType}" : JobType;
    [JsonIgnore] public Chip StatusChip => Choices.Status(Status);
    [JsonIgnore] public bool IsOpen => Status != "Completed";
    [JsonIgnore] public bool IsOverdue => IsOpen && DueDate < Today;
    [JsonIgnore] public string Due => Date(DueDate);
    [JsonIgnore] public string Hours => $"{ActualHours:0.#} / {BudgetHours:0.#} h";

    public override object Columns() => new { OrgId, ClientId, Title = Title.Trim(), JobType, Status, DueDate, AssigneeId, BudgetHours, ActualHours };
}

public sealed class Project : Row
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Status { get; set; } = "Open";
    public DateOnly? StartDate { get; set; }
    public DateOnly? DueDate { get; set; }
    public Guid? OwnerId { get; set; }
    public int Progress { get; set; }

    [JsonIgnore] public string OwnerName { get; set; } = "";
    [JsonIgnore] public Guid OwnerKey { get => Key(OwnerId); set => OwnerId = FromKey(value); }
    [JsonIgnore] public Chip StatusChip => Choices.Status(Status);
    [JsonIgnore] public bool IsOverdue => Status != "Closed" && DueDate < Today;
    [JsonIgnore] public string Due => Date(DueDate);

    public override object Columns() => new { OrgId, Name = Name.Trim(), Description = Description.Trim(), Status, StartDate, DueDate, OwnerId, Progress = Math.Clamp(Progress, 0, 100) };
}

// A task row; named WorkTask so it doesn't clash with System.Threading.Tasks.Task.
public sealed class WorkTask : Row
{
    public Guid? ProjectId { get; set; }
    public Guid? JobId { get; set; }
    public string Title { get; set; } = "";
    public string Status { get; set; } = "Backlog";
    public string Priority { get; set; } = "Medium";
    public Guid? AssigneeId { get; set; }
    public DateOnly? DueDate { get; set; }
    public int Points { get; set; }
    public int SortOrder { get; set; }

    [JsonIgnore] public string ProjectName { get; set; } = "";
    [JsonIgnore] public string JobTitle { get; set; } = "";
    [JsonIgnore] public string AssigneeName { get; set; } = "";
    [JsonIgnore] public Guid ProjectKey { get => Key(ProjectId); set => ProjectId = FromKey(value); }
    [JsonIgnore] public Guid JobKey { get => Key(JobId); set => JobId = FromKey(value); }
    [JsonIgnore] public Guid AssigneeKey { get => Key(AssigneeId); set => AssigneeId = FromKey(value); }
    [JsonIgnore] public Chip StatusChip => Choices.Status(Status);
    [JsonIgnore] public Chip PriorityChip => new(Priority, Priority switch { "High" => "Red", "Medium" => "Amber", _ => "Grey" });
    [JsonIgnore] public bool IsOpen => Status != "Done";
    [JsonIgnore] public bool IsOverdue => IsOpen && DueDate < Today;
    [JsonIgnore] public string Due => Date(DueDate);
    [JsonIgnore] public string Initials => string.Concat(AssigneeName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(w => char.ToUpperInvariant(w[0])));
    [JsonIgnore] public bool HasAssignee => AssigneeId is not null;

    public override object Columns() => new { OrgId, ProjectId, JobId, Title = Title.Trim(), Status, Priority, AssigneeId, DueDate, Points = Math.Max(0, Points), SortOrder };
}

public sealed class Report : Row
{
    public string Title { get; set; } = "";
    public string Kind { get; set; } = "Ratio study";
    public string Status { get; set; } = "Draft";
    public Guid? AuthorId { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public string Summary { get; set; } = "";

    [JsonIgnore] public string AuthorName { get; set; } = "";
    [JsonIgnore] public Guid AuthorKey { get => Key(AuthorId); set => AuthorId = FromKey(value); }
    [JsonIgnore] public Chip StatusChip => Choices.Status(Status);
    [JsonIgnore] public DateOnly CreatedOn => DateOnly.FromDateTime(CreatedAt.LocalDateTime);
    [JsonIgnore] public DateOnly? ClosedOn => ClosedAt is { } at ? DateOnly.FromDateTime(at.LocalDateTime) : null;
    [JsonIgnore] public string Created => CreatedAt == default ? "—" : Date(CreatedOn);
    [JsonIgnore] public string Closed => Date(ClosedOn);

    // Closing stamps the time once; reopening clears it.
    public override object Columns() => new { OrgId, Title = Title.Trim(), Kind, Status, AuthorId, ClosedAt = Status == "Closed" ? ClosedAt ?? DateTimeOffset.UtcNow : (DateTimeOffset?)null, Summary = Summary.Trim() };
}

// One person in a company (public.team_members); Id is their user id. Saved through update_member and add_member, not the table.
public sealed class Member : Row
{
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Role { get; set; } = "member";
    public string Status { get; set; } = "active";
    public string JobTitle { get; set; } = "";
    public DateTimeOffset? DecidedAt { get; set; }

    // The database keeps lower-case values; the form shows them capitalised.
    [JsonIgnore] public string RoleLabel { get => Role == "admin" ? "Admin" : "Member"; set => Role = value == "Admin" ? "admin" : "member"; }
    [JsonIgnore] public string StatusLabel { get => Status.Length == 0 ? "" : char.ToUpperInvariant(Status[0]) + Status[1..]; set => Status = (value ?? "").ToLowerInvariant(); }
    [JsonIgnore] public Chip StatusChip => new(StatusLabel, Status switch { "pending" => "Amber", "active" => "Green", "rejected" => "Red", _ => "Grey" });
    [JsonIgnore] public Chip RoleChip => new(RoleLabel, Role == "admin" ? "Purple" : "Grey");
    [JsonIgnore] public bool IsPending => Status == "pending";
    [JsonIgnore] public bool IsNewPerson => Id == Guid.Empty;
    [JsonIgnore] public bool IsExisting => Id != Guid.Empty;
    [JsonIgnore] public string Name => FullName.Length > 0 ? FullName : Email;
    [JsonIgnore] public string Title => JobTitle.Length > 0 ? JobTitle : "—";

    public override object Columns() => new { Role, Status, JobTitle };
}

// A company using Levy (public.list_companies), for platform admins. Saved through save_company.
public sealed class Company : Row
{
    public string Name { get; set; } = "";
    public string JoinCode { get; set; } = "";
    public int Members { get; set; }
    public int Pending { get; set; }
    public int Admins { get; set; }
    public Guid? AccountManagerId { get; set; }
    public string AccountManagerName { get; set; } = "";

    // Only used when creating: start the company with the sample clients, jobs and tasks.
    [JsonIgnore] public bool WithSampleData { get; set; }
    [JsonIgnore] public Guid AccountManagerKey { get => Key(AccountManagerId); set => AccountManagerId = FromKey(value); }
    [JsonIgnore] public string Manager => AccountManagerName.Length > 0 ? AccountManagerName : "—";
    [JsonIgnore] public bool IsExisting => Id != Guid.Empty;
    [JsonIgnore] public bool IsNewCompany => Id == Guid.Empty;
    [JsonIgnore] public string State => Pending > 0 ? "Waiting for review" : Admins == 0 ? "No admin" : "All set";
    [JsonIgnore] public Chip StateChip => new(Pending > 0 ? $"{Pending} waiting" : State, Pending > 0 ? "Amber" : Admins == 0 ? "Red" : "Green");
    [JsonIgnore] public string Created => CreatedAt == default ? "—" : Date(DateOnly.FromDateTime(CreatedAt.LocalDateTime));

    public override object Columns() => new { Name };
}

// One entry in the activity log (public.activity): who did what to which record, and when. Written by database triggers; read-only here.
public sealed class ActivityEntry : Row
{
    public Guid? ActorId { get; set; }
    public string ActorName { get; set; } = "";
    public string Action { get; set; } = "";
    public string Entity { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Details { get; set; } = "";

    [JsonIgnore] public DateTime When => CreatedAt.LocalDateTime;
    [JsonIgnore] public string Day => Date(DateOnly.FromDateTime(When));
    [JsonIgnore] public string Time => When.ToString("h:mm tt", CultureInfo.GetCultureInfo("en-US"));
    [JsonIgnore] public Chip EntityChip => new(Entity, Entity switch { "Job" => "Blue", "Task" => "Purple", "Person" => "Amber", "Report" => "Green", "Request" => "Red", _ => "Grey" });
    [JsonIgnore] public string DetailsOrDash => Details.Length > 0 ? Details : "—";

    public override object Columns() => new { };
}

// A merge request linked to a request's GitLab issue, as last read from GitLab (stored in issue_reports.merge_requests).
public sealed record MergeRequestInfo(int Iid, string Title, string State, string Url, bool Draft, string? Pipeline)
{
    [JsonIgnore] public string Summary => $"!{Iid} {Title} · {(State == "opened" && Draft ? "draft" : State)}{(Pipeline is { } p ? " · pipeline " + p : "")}";
}

// A bug report or feature request (public.issue_reports): filed by a company's staff, triaged by its account manager, worked in GitLab.
// OrgName is filled only on the Intake page (public.list_intake), where requests from several companies appear together.
public sealed class IssueReport : Row
{
    public Guid? ReportedBy { get; set; }
    public string ReporterName { get; set; } = "";
    public string? OrgName { get; set; }
    public string Title { get; set; } = "";
    public string Kind { get; set; } = "Bug";
    public string Description { get; set; } = "";
    public string Context { get; set; } = "";
    public string Status { get; set; } = "New";
    public string? Priority { get; set; }
    public string? Stack { get; set; }
    public string TriageNote { get; set; } = "";
    public string? GitlabProject { get; set; }
    public int? GitlabIid { get; set; }
    public string? GitlabUrl { get; set; }
    public string? GitlabState { get; set; }
    public List<MergeRequestInfo> MergeRequests { get; set; } = [];
    public DateTimeOffset UpdatedAt { get; set; }

    [JsonIgnore] public bool IsNewReport => Id == Guid.Empty;
    [JsonIgnore] public bool IsFiled => Id != Guid.Empty;
    [JsonIgnore] public bool IsInGitLab => GitlabIid is not null;
    [JsonIgnore] public bool CanSend => IsFiled && GitlabIid is null;
    [JsonIgnore] public bool IsOpen => Status is not ("Fixed" or "Closed" or "Declined");
    [JsonIgnore] public Chip StatusChip => new(Status, Status switch
    {
        "New" => "Amber", "Triaged" or "In GitLab" => "Blue", "In progress" or "In review" => "Purple", "Fixed" => "Green", "Declined" => "Red", _ => "Grey",
    });
    [JsonIgnore] public Chip KindChip => new(Kind, Kind switch { "Bug" => "Red", "Feature" => "Blue", _ => "Grey" });
    [JsonIgnore] public Chip PriorityChip => new(Priority ?? "Not set", Priority switch { "Critical" or "High" => "Red", "Medium" => "Amber", _ => "Grey" });
    [JsonIgnore] public string Filed => CreatedAt == default ? "—" : Date(DateOnly.FromDateTime(CreatedAt.LocalDateTime));
    [JsonIgnore] public string Where => GitlabIid is { } iid ? $"{GitlabProject}#{iid}" : "—";
    [JsonIgnore] public string Progress => MergeRequests.Count == 0 ? (IsInGitLab ? "No merge request yet" : "Your account manager sends it to the developers after triage.") : string.Join("\n", MergeRequests.Select(m => m.Summary));
    [JsonIgnore] public string IssueNumber => GitlabIid is { } iid ? $"#{iid}" : "—";
    [JsonIgnore] public string NoteOrDash => TriageNote.Length > 0 ? TriageNote : "—";

    // Filing sends only what the reporter writes; the database fills in who, when and the starting status.
    public override object Columns() => new { OrgId, Title = Title.Trim(), Kind, Description = Description.Trim(), Context = Context.Trim() };

    // What the account manager changes while triaging.
    public object TriageColumns() => new { Title = Title.Trim(), Kind, Description = Description.Trim(), Status, Priority, Stack, TriageNote = TriageNote.Trim() };

    // Triage plus where the request lives in GitLab and what GitLab says about it.
    public object GitLabColumns() => new { Title = Title.Trim(), Kind, Description = Description.Trim(), Status, Priority, Stack, TriageNote = TriageNote.Trim(),
        GitlabProject, GitlabIid, GitlabUrl, GitlabState, MergeRequests };
}
