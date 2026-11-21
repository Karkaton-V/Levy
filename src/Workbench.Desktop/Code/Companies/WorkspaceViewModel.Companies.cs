using System.Windows.Input;

namespace Workbench.Desktop;

// Companies page (platform admins): every company using Levy, creating and renaming them, and assigning people to them by email.
public sealed partial class WorkspaceViewModel
{
    string _assignEmail = "";
    string _assignRole = "Admin";
    string _assignTitle = "";
    string _assignMessage = "";
    bool _assignFailed;
    bool _assigning;

    public RecordPage<Company> Companies { get; }

    // The "Add a person" fields on the selected company.
    public string AssignEmail { get => _assignEmail; set { _assignEmail = value; Raise(); } }
    public string AssignRole { get => _assignRole; set { _assignRole = value; Raise(); } }
    public string AssignTitle { get => _assignTitle; set { _assignTitle = value; Raise(); } }
    public string AssignMessage { get => _assignMessage; private set { _assignMessage = value; Raise(); Raise(nameof(HasAssignMessage)); } }
    public bool AssignFailed { get => _assignFailed; private set { _assignFailed = value; Raise(); } }
    public bool HasAssignMessage => _assignMessage.Length > 0;
    public string AssignLabel => _assigning ? "Adding…" : "Add to company";
    public ICommand AssignCommand => new RelayCommand(() => _ = AssignAsync());

    RecordPage<Company> CreateCompanies()
    {
        var page = new RecordPage<Company>(this, new PageRules<Company>(
            Noun: "company",
            Plural: "companies",
            FilterValues: Choices.CompanyStates,
            AllLabel: "All companies",
            SearchText: c => $"{c.Name} {c.JoinCode}",
            FilterValue: c => c.State,
            Mine: null,
            Create: () => new Company(),
            Validate: c => Required(c.Name, "company"),
            Counters: rows =>
            [
                Counter("Companies", rows.Count, "using Levy"),
                Counter("People", rows.Sum(c => c.Members), "active accounts"),
                Counter("Waiting for review", rows.Sum(c => c.Pending), "across companies"),
                Counter("Without an admin", rows.Count(c => c.Admins == 0), "need one assigned"),
            ]), SaveCompanyAsync, _me);
        page.Saved += OnCompanySaved;
        page.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(page.Draft)) ClearAssign(); };
        return page;
    }

    async Task<Company> SaveCompanyAsync(Company draft, bool isNew) =>
        (await _client.RpcAsync<Company>("save_company", new { Company = isNew ? (Guid?)null : draft.Id, CompanyName = draft.Name.Trim(), WithSampleData = isNew && draft.WithSampleData })).Single();

    // Renaming the user's own company updates the page subtitle.
    void OnCompanySaved()
    {
        if (Companies.Items.FirstOrDefault(c => c.Id == OrganizationId) is { } own && own.Name != _membership.OrgName)
        {
            _membership = _membership with { OrgName = own.Name };
            Raise(nameof(OrganizationName));
        }
        ClearAssign();
        Companies.Refresh();
    }

    // Adds an existing account to the selected company (active straight away), then refreshes its head count.
    async Task AssignAsync()
    {
        if (_assigning || Companies.Draft is not { IsExisting: true } company) return;
        if (!_assignEmail.Contains('@')) { ShowAssign("Enter the email address they signed up with.", failed: true); return; }
        _assigning = true;
        Raise(nameof(AssignLabel));
        try
        {
            var role = _assignRole == "Admin" ? "admin" : "member";
            var person = (await _client.RpcAsync<Member>("add_member", new { Org = company.Id, MemberEmail = _assignEmail.Trim(), NewRole = role, NewJobTitle = _assignTitle.Trim() })).Single();
            var refreshed = (await _client.RpcAsync<Company>("list_companies")).FirstOrDefault(c => c.Id == company.Id);
            if (refreshed is not null) { Companies.Replace(refreshed); Companies.Refresh(); Companies.Open(refreshed); }
            if (company.Id == OrganizationId) Team.Load(await _client.RpcAsync<Member>("team_members", new { Org = OrganizationId }));
            AssignEmail = AssignTitle = "";
            ShowAssign($"{person.Name} is now {(role == "admin" ? "an admin" : "a member")} of {company.Name}.", failed: false);
        }
        catch (SupabaseException e) { ShowAssign(e.Message, failed: true); }
        finally
        {
            _assigning = false;
            Raise(nameof(AssignLabel));
        }
    }

    void ShowAssign(string message, bool failed)
    {
        AssignFailed = failed;
        AssignMessage = message;
    }

    void ClearAssign() => AssignMessage = "";
}
