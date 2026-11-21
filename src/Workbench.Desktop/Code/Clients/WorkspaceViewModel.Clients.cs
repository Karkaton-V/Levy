namespace Workbench.Desktop;

// Clients page: the people and bodies the office does work for.
public sealed partial class WorkspaceViewModel
{
    public RecordPage<Client> Clients { get; }

    RecordPage<Client> CreateClients() => new(this, new PageRules<Client>(
        Noun: "client",
        FilterValues: Choices.ClientKinds,
        AllLabel: "All kinds",
        SearchText: c => $"{c.Name} {c.ParcelId} {c.City} {c.Email} {c.Phone}",
        FilterValue: c => c.Kind,
        Mine: null,
        Create: () => new Client(),
        Validate: c => Required(c.Name, "client"),
        Counters: rows =>
        [
            Counter("Clients", rows.Count, "on file"),
            Counter("Businesses", rows.Count(c => c.Kind == "Business"), "commercial owners"),
            Counter("Individuals", rows.Count(c => c.Kind == "Individual"), "homeowners"),
            Counter("With open jobs", rows.Count(c => c.OpenJobs > 0), "active work"),
        ]), (draft, isNew) => SaveAsync("clients", draft, isNew), _me);
}
