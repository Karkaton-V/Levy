using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Workbench.Desktop;

// The workflow pages' data: the user's organization, its people and its rows, read from and saved to Supabase.
// Each page adds its part in Code/<Page>/WorkspaceViewModel.<Page>.cs.
public sealed partial class WorkspaceViewModel : INotifyPropertyChanged
{
    readonly SupabaseClient _client;
    readonly Guid _me;
    Membership _membership;
    string _message = "Loading your workspace…";

    public WorkspaceViewModel(SupabaseClient client, Membership membership)
    {
        _client = client;
        _membership = membership;
        _me = Guid.TryParse(client.User?.Id, out var id) ? id : Guid.Empty;
        Clients = CreateClients();
        Jobs = CreateJobs();
        Projects = CreateProjects();
        Tasks = CreateTasks();
        Reports = CreateReports();
        Team = CreateTeam();
        Companies = CreateCompanies();
        Activity = CreateActivity();
        Requests = CreateRequests();
        Intake = CreateIntake();
        Clients.Saved += Relink; Jobs.Saved += Relink; Projects.Saved += Relink; Tasks.Saved += Relink; Reports.Saved += Relink;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    // The company this window works in, and the user's place in it.
    public Guid OrganizationId => _membership.OrgId;
    public string OrganizationName => _membership.OrgName;
    public bool IsAdmin => _membership.IsAdmin;
    public bool IsPlatformAdmin => _membership.IsPlatformAdmin;
    public string JoinCode => _membership.JoinCode ?? "";

    // Under the logo in the rail: the job title an admin gave them, or their role until they have one.
    public string JobTitle => _membership.JobTitle.Length > 0 ? _membership.JobTitle : IsAdmin ? "Administrator" : "Team member";

    // "Loading…" or a load error, shown over the tables; empty once the rows are in.
    public string Message { get => _message; private set { _message = value; Raise(); Raise(nameof(HasMessage)); } }
    public bool HasMessage => _message.Length > 0;

    // Picker choices, each led by an "Unassigned" / "None" entry.
    public IReadOnlyList<Option> People { get; private set; } = [];
    public IReadOnlyList<Option> ClientOptions { get; private set; } = [];
    public IReadOnlyList<Option> ProjectOptions { get; private set; } = [];
    public IReadOnlyList<Option> JobOptions { get; private set; } = [];

    List<Person> _people = [];

    // Reads everything the pages show in one go; row-level security limits each table to the user's organization.
    public async Task LoadAsync()
    {
        Message = "Loading your workspace…";
        try
        {
            var people = _client.GetAsync<Person>("profiles", "select=id,full_name,email&order=full_name");
            var clients = _client.GetAsync<Client>("clients", "select=*&order=name");
            var jobs = _client.GetAsync<Job>("jobs", "select=*&order=due_date.nullslast");
            var projects = _client.GetAsync<Project>("projects", "select=*&order=due_date.nullslast");
            var tasks = _client.GetAsync<WorkTask>("tasks", "select=*&order=sort_order,due_date.nullslast");
            var reports = _client.GetAsync<Report>("reports", "select=*&order=created_at.desc");
            await System.Threading.Tasks.Task.WhenAll(people, clients, jobs, projects, tasks, reports);

            _people = people.Result;
            Clients.Load(clients.Result);
            Jobs.Load(jobs.Result);
            Projects.Load(projects.Result);
            Tasks.Load(tasks.Result);
            Reports.Load(reports.Result);
            Relink();
            Message = "";
        }
        catch (SupabaseException e) { Message = e.Message; }
        await LoadRequestsAsync();
        await LoadAdminAsync();
    }

    // Fills names and counts from the rows as they are now, rebuilds the pickers and the board, and refreshes every page.
    void Relink()
    {
        string PersonName(Guid? id) => id is null ? "" : _people.FirstOrDefault(p => p.Id == id)?.Name ?? "";
        var clientNames = Clients.Items.ToDictionary(c => c.Id, c => c.Name);
        var projectNames = Projects.Items.ToDictionary(p => p.Id, p => p.Name);
        var jobTitles = Jobs.Items.ToDictionary(j => j.Id, j => j.Title);
        string Lookup(Dictionary<Guid, string> names, Guid? id) => id is { } key && names.TryGetValue(key, out var name) ? name : "";

        foreach (var client in Clients.Items) client.OpenJobs = Jobs.Items.Count(j => j.ClientId == client.Id && j.IsOpen);
        foreach (var job in Jobs.Items) { job.ClientName = Lookup(clientNames, job.ClientId); job.AssigneeName = PersonName(job.AssigneeId); }
        foreach (var project in Projects.Items) project.OwnerName = PersonName(project.OwnerId);
        foreach (var task in Tasks.Items)
        {
            task.ProjectName = Lookup(projectNames, task.ProjectId);
            task.JobTitle = Lookup(jobTitles, task.JobId);
            task.AssigneeName = PersonName(task.AssigneeId);
        }
        foreach (var report in Reports.Items) report.AuthorName = PersonName(report.AuthorId);

        People = [new(Guid.Empty, "Unassigned"), .. _people.Select(p => new Option(p.Id, p.Name))];
        ClientOptions = [new(Guid.Empty, "None"), .. Clients.Items.OrderBy(c => c.Name).Select(c => new Option(c.Id, c.Name))];
        ProjectOptions = [new(Guid.Empty, "None"), .. Projects.Items.OrderBy(p => p.Name).Select(p => new Option(p.Id, p.Name))];
        JobOptions = [new(Guid.Empty, "None"), .. Jobs.Items.OrderBy(j => j.Title).Select(j => new Option(j.Id, j.Title))];
        Raise(nameof(People)); Raise(nameof(ClientOptions)); Raise(nameof(ProjectOptions)); Raise(nameof(JobOptions));

        Clients.Refresh(); Jobs.Refresh(); Projects.Refresh(); Tasks.Refresh(); Reports.Refresh();
        RebuildBoard();
    }

    // Inserts a new row or updates an existing one in the user's organization and returns it as stored.
    async Task<T> SaveAsync<T>(string table, T draft, bool isNew) where T : Row
    {
        draft.OrgId = _membership.OrgId;
        return isNew ? await _client.InsertAsync<T>(table, draft.Columns()) : await _client.UpdateAsync<T>(table, draft.Id, draft.Columns());
    }

    static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

    // A counter card for a page's summary row.
    static SummaryCard Counter(string label, int value, string detail) => new(label, value.ToString("N0"), detail, "", false, false);

    static string? Required(string text, string field) => string.IsNullOrWhiteSpace(text) ? $"Give the {field} a name." : null;

    void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
