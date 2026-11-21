using System.Windows.Input;

namespace Workbench.Desktop;

// Team page (company admins): review people who asked to join with the company code, set job titles and roles, turn access off, add existing accounts.
public sealed partial class WorkspaceViewModel
{
    string _adminMessage = "";

    public RecordPage<Member> Team { get; }

    // Why the admin pages couldn't load, shown over their tables.
    public string AdminMessage { get => _adminMessage; private set { _adminMessage = value; Raise(); Raise(nameof(HasAdminMessage)); } }
    public bool HasAdminMessage => _adminMessage.Length > 0;

    public ICommand ApproveCommand => new RelayCommand(() => Decide("active"));
    public ICommand RejectCommand => new RelayCommand(() => Decide("rejected"));

    RecordPage<Member> CreateTeam()
    {
        var page = new RecordPage<Member>(this, new PageRules<Member>(
            Noun: "person",
            Plural: "people",
            NewText: "Add person",
            FilterValues: Choices.MemberStatuses,
            AllLabel: "Everyone",
            SearchText: m => $"{m.FullName} {m.Email} {m.JobTitle}",
            FilterValue: m => m.StatusLabel,
            Mine: null,
            Create: () => new Member(),
            Validate: m => m.IsNewPerson && !m.Email.Contains('@') ? "Enter the email address they signed up with." : null,
            Counters: rows =>
            [
                Counter("Waiting for review", rows.Count(m => m.Status == "pending"), "asked to join"),
                Counter("Active", rows.Count(m => m.Status == "active"), "can sign in"),
                Counter("Admins", rows.Count(m => m.Status == "active" && m.Role == "admin"), "review people"),
                Counter("Turned off", rows.Count(m => m.Status is "rejected" or "disabled"), "declined or disabled"),
            ]), SaveMemberAsync, _me);
        page.Saved += OnTeamSaved;
        return page;
    }

    // New people are added by email (an account they already made); everyone else is updated in place.
    async Task<Member> SaveMemberAsync(Member draft, bool isNew)
    {
        var rows = isNew
            ? await _client.RpcAsync<Member>("add_member", new { Org = OrganizationId, MemberEmail = draft.Email.Trim(), NewRole = draft.Role, NewJobTitle = draft.JobTitle })
            : await _client.RpcAsync<Member>("update_member", new { Org = OrganizationId, Member = draft.Id, NewRole = draft.Role, NewStatus = draft.Status, NewJobTitle = draft.JobTitle });
        return rows.Single();
    }

    // An approved person becomes someone work can be assigned to; a change to the user's own row updates the rail.
    void OnTeamSaved()
    {
        foreach (var member in Team.Items.Where(m => m.Status == "active" && _people.All(p => p.Id != m.Id)))
            _people.Add(new Person { Id = member.Id, FullName = member.FullName, Email = member.Email });
        if (Team.Items.FirstOrDefault(m => m.Id == _me) is { } self)
        {
            _membership = _membership with { JobTitle = self.JobTitle, Role = self.Role, Status = self.Status };
            Raise(nameof(JobTitle)); Raise(nameof(IsAdmin));
        }
        Relink();
        Team.Refresh();
    }

    // Approve / Decline on a pending person: set the status and save.
    void Decide(string status)
    {
        if (Team.Draft is not { } draft) return;
        draft.Status = status;
        Team.SaveCommand.Execute(null);
    }

    // The Team and Companies lists, for those allowed to see them.
    async Task LoadAdminAsync()
    {
        try
        {
            if (IsAdmin || IsPlatformAdmin) Team.Load(await _client.RpcAsync<Member>("team_members", new { Org = OrganizationId }));
            if (IsPlatformAdmin)
            {
                Companies.Load(await _client.RpcAsync<Company>("list_companies"));
                StaffOptions = [new(Guid.Empty, "None"), .. (await _client.RpcAsync<Person>("list_staff")).Select(p => new Option(p.Id, p.Name))];
                Raise(nameof(StaffOptions));
            }
            AdminMessage = "";
        }
        catch (SupabaseException e) { AdminMessage = e.Message; }
    }
}
