using System.Windows.Input;

namespace Workbench.Desktop;

// The company tag (an icon and up to four letters) shown next to everyone's name on their profile card; admins set it on the Team page.
public sealed partial class WorkspaceViewModel
{
    public const int TagLength = 4;

    sealed record OrgTag(Guid Id, string? TagIcon, string? TagText);

    string? _tagIcon, _tagText;
    CompanyTagIcon? _draftTagIcon;
    string _draftTagText = "";
    string _tagMessage = "";
    bool _tagFailed;

    // The tag as saved: what the profile cards show.
    public string? TagIcon { get => _tagIcon; private set { _tagIcon = value; Raise(); } }
    public string? TagText { get => _tagText; private set { _tagText = value; Raise(); } }

    // The tag being edited, and how it will look.
    public CompanyTagIcon? DraftTagIcon { get => _draftTagIcon; set { _draftTagIcon = value; Raise(); RaiseTagPreview(); } }
    public string DraftTagText
    {
        get => _draftTagText;
        set { _draftTagText = new string((value ?? "").ToUpperInvariant().Where(char.IsAsciiLetterOrDigit).Take(TagLength).ToArray()); Raise(); RaiseTagPreview(); }
    }
    public string DraftTagGlyph => _draftTagIcon?.Glyph ?? "";
    public bool HasDraftTag => _draftTagIcon is not null && _draftTagText.Length > 0;

    public string TagMessage { get => _tagMessage; private set { _tagMessage = value; Raise(); } }
    public bool TagFailed { get => _tagFailed; private set { _tagFailed = value; Raise(); } }

    public ICommand SaveTagCommand => new RelayCommand(() => _ = SaveTagAsync(_draftTagIcon?.Key, _draftTagText));
    public ICommand ClearTagCommand => new RelayCommand(() => _ = SaveTagAsync(null, ""));

    // Everyone reads the tag (their own card shows it); a failure just leaves the card without one.
    async Task LoadTagAsync()
    {
        try
        {
            var org = (await _client.GetAsync<OrgTag>("organizations", $"select=id,tag_icon,tag_text&id=eq.{OrganizationId}")).FirstOrDefault();
            ShowSavedTag(org?.TagIcon, org?.TagText);
        }
        catch (SupabaseException) { }
    }

    async Task SaveTagAsync(string? icon, string text)
    {
        if (icon is null != (text.Length == 0)) { ShowTag("Pick an icon and type up to four letters, or press Remove.", failed: true); return; }
        try
        {
            var org = await _client.UpdateAsync<OrgTag>("organizations", OrganizationId, new { TagIcon = icon, TagText = icon is null ? null : text });
            ShowSavedTag(org.TagIcon, org.TagText);
            ShowTag(org.TagIcon is null ? "Company tag removed." : "Company tag saved. It shows on everyone's profile card.", failed: false);
        }
        catch (SupabaseException e) { ShowTag(e.Message, failed: true); }
    }

    void ShowSavedTag(string? icon, string? text)
    {
        TagIcon = icon;
        TagText = text;
        DraftTagIcon = CompanyTagIcon.Find(icon);
        DraftTagText = text ?? "";
    }

    void ShowTag(string message, bool failed)
    {
        TagFailed = failed;
        TagMessage = message;
    }

    void RaiseTagPreview() { Raise(nameof(DraftTagGlyph)); Raise(nameof(HasDraftTag)); }
}
