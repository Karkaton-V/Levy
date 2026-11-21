namespace Workbench.Desktop;

// The signed-in user's place in one company (public.my_memberships): role, review status and job title. JoinCode is filled for admins only.
// IsAccountManager: the user manages at least one company's requests; AccountManagerName: who manages this company's.
public sealed record Membership(Guid OrgId, string OrgName, string Role, string Status, string JobTitle, string? JoinCode, bool IsPlatformAdmin, DateTimeOffset JoinedAt,
    bool IsAccountManager = false, string AccountManagerName = "")
{
    public bool IsActive => Status == "active";
    public bool IsAdmin => IsActive && Role == "admin";

    // The company to open, or a sentence saying why there isn't one (waiting for review, declined, turned off).
    public static async Task<(Membership? Active, string? Problem)> CheckAsync(SupabaseClient client)
    {
        var all = await client.RpcAsync<Membership>("my_memberships");
        if (all.FirstOrDefault(m => m.IsActive) is { } active) return (active, null);
        var latest = all.OrderByDescending(m => m.JoinedAt).FirstOrDefault();
        return (null, latest?.Status switch
        {
            "pending" => $"Your request to join {latest.OrgName} is waiting for one of its admins to approve it. Try again once they have.",
            "rejected" => $"An admin of {latest.OrgName} declined your request to join. Ask them for help, or create a new account without a company code.",
            "disabled" => $"Your access to {latest.OrgName} has been turned off. Ask one of its admins to turn it back on.",
            _ => "Your account isn't part of a company yet. Ask a company admin to add you.",
        });
    }
}
