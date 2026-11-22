using System.Windows.Input;

namespace Workbench.Desktop;

// Team page, continued: badges admins award and take back, and deleting the account of someone who works only under them.
public sealed partial class WorkspaceViewModel
{
    List<BadgeAward> _teamAwards = [];
    BadgeInfo? _awardBadge;
    string _awardReason = "";
    string _teamMessage = "";
    bool _teamFailed;
    bool _teamBusy;

    // The badges of the person open in the Team form, newest first.
    public IReadOnlyList<BadgeAward> DraftBadges => Team.Draft is { IsExisting: true } draft
        ? _teamAwards.Where(a => a.UserId == draft.Id).OrderByDescending(a => a.CreatedAt).ToList() : [];
    public bool HasDraftBadges => DraftBadges.Count > 0;

    // Badges go to active members other than yourself; accounts can be deleted for members (not admins, not yourself).
    public bool CanAwardBadges => Team.Draft is { IsExisting: true, Status: "active" } draft && draft.Id != _me;
    public bool CanDeleteDraft => Team.Draft is { IsExisting: true, Role: "member" } draft && draft.Id != _me;

    public BadgeInfo? AwardBadge { get => _awardBadge; set { _awardBadge = value; Raise(); } }
    public string AwardReason { get => _awardReason; set { _awardReason = value; Raise(); } }
    public string TeamMessage { get => _teamMessage; private set { _teamMessage = value; Raise(); Raise(nameof(HasTeamMessage)); } }
    public bool TeamFailed { get => _teamFailed; private set { _teamFailed = value; Raise(); } }
    public bool HasTeamMessage => _teamMessage.Length > 0;

    public ICommand AwardBadgeCommand => new RelayCommand(() => _ = AwardBadgeAsync());
    public ICommand RevokeBadgeCommand => new ParameterCommand(award => { if (award is BadgeAward a) _ = RevokeBadgeAsync(a); });

    // Called once Team exists: the badge section follows whoever is open in the form.
    void FollowTeamDraft() => Team.PropertyChanged += (_, e) =>
    {
        if (e.PropertyName != nameof(Team.Draft)) return;
        TeamMessage = "";
        AwardReason = "";
        RaiseBadges();
    };

    async Task LoadTeamAwardsAsync()
    {
        _teamAwards = await _client.GetAsync<BadgeAward>("badge_awards", $"select=*&org_id=eq.{OrganizationId}");
        RaiseBadges();
    }

    async Task AwardBadgeAsync()
    {
        if (_teamBusy || Team.Draft is not { IsExisting: true } person) return;
        if (_awardBadge is null) { ShowTeam("Pick a badge to award.", failed: true); return; }
        _teamBusy = true;
        try
        {
            var award = (await _client.RpcAsync<BadgeAward>("award_badge", new { Org = OrganizationId, Member = person.Id, BadgeKey = _awardBadge.Key, Why = _awardReason.Trim() })).Single();
            _teamAwards.Add(award);
            ShowTeam($"Awarded \"{_awardBadge.Name}\" to {person.Name}.", failed: false);
            AwardBadge = null;
            AwardReason = "";
            RaiseBadges();
        }
        catch (SupabaseException e) { ShowTeam(e.Message, failed: true); }
        finally { _teamBusy = false; }
    }

    async Task RevokeBadgeAsync(BadgeAward award)
    {
        if (_teamBusy) return;
        _teamBusy = true;
        try
        {
            await _client.CallAsync("revoke_badge", new { Award = award.Id });
            _teamAwards.RemoveAll(a => a.Id == award.Id);
            ShowTeam($"Took back \"{award.Name}\".", failed: false);
            RaiseBadges();
        }
        catch (SupabaseException e) { ShowTeam(e.Message, failed: true); }
        finally { _teamBusy = false; }
    }

    // Permanently deletes the account open in the form (the view asks for confirmation first). Their work stays, unassigned.
    public async Task<bool> DeleteDraftAccountAsync()
    {
        if (_teamBusy || Team.Draft is not { IsExisting: true } person) return false;
        _teamBusy = true;
        try
        {
            await _client.CallAsync("delete_account", new { Org = OrganizationId, Member = person.Id });
            var row = Team.Items.FirstOrDefault(m => m.Id == person.Id);
            if (row is not null) Team.Items.Remove(row);
            _teamAwards.RemoveAll(a => a.UserId == person.Id);
            _people.RemoveAll(p => p.Id == person.Id);
            Team.CancelCommand.Execute(null);
            Team.Refresh();
            Relink();
            return true;
        }
        catch (SupabaseException e) { ShowTeam(e.Message, failed: true); return false; }
        finally { _teamBusy = false; }
    }

    void ShowTeam(string message, bool failed)
    {
        TeamFailed = failed;
        TeamMessage = message;
    }

    void RaiseBadges()
    {
        Raise(nameof(DraftBadges)); Raise(nameof(HasDraftBadges)); Raise(nameof(CanAwardBadges)); Raise(nameof(CanDeleteDraft));
    }
}
