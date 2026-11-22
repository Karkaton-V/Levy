using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace Workbench.Desktop;

// Edits the signed-in person's profile. Changes show in the preview card as they're made and are saved together (text, banner colour,
// new or removed pictures); Cancel leaves everything as it was.
public partial class EditProfileWindow : Window
{
    static readonly string[] BannerColors = ["blue", "teal", "green", "amber", "rose", "purple", "slate"];
    const string ImageFilter = "Images (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg";

    readonly ProfileStore _store;
    readonly ProfileCard _preview;
    string _bannerColor;
    byte[]? _newAvatar;
    byte[]? _newBanner;
    bool _removeAvatar;
    bool _removeBanner;

    public EditProfileWindow(ProfileStore store)
    {
        InitializeComponent();
        _store = store;
        _preview = store.Card.CopyForPreview();
        _bannerColor = store.Card.BannerColor;
        PreviewCard.DataContext = _preview;
        NameBox.Text = store.Card.Name;
        PronounsBox.ItemsSource = ProfileCard.PronounChoices.Select(p => p == "" ? "None" : p).ToList();
        PronounsBox.SelectedIndex = Math.Max(0, Array.IndexOf(ProfileCard.PronounChoices, store.Card.Pronouns));
        BioBox.Text = store.Card.Bio;
        foreach (var color in BannerColors)
        {
            var swatch = new RadioButton { Style = (Style)FindResource("BannerSwatch"), GroupName = "Banner", IsChecked = color == _bannerColor, Tag = color, ToolTip = char.ToUpperInvariant(color[0]) + color[1..] };
            swatch.SetResourceReference(BackgroundProperty, "Banner" + char.ToUpperInvariant(color[0]) + color[1..]);
            swatch.Checked += (_, _) => { _bannerColor = (string)swatch.Tag; _preview.BannerColor = _bannerColor; };
            Swatches.Children.Add(swatch);
        }
        ShowButtons();
        Loaded += (_, _) => NameBox.Focus();
    }

    string Pronouns => ProfileCard.PronounChoices[Math.Max(0, PronounsBox.SelectedIndex)];

    void Preview(object sender, RoutedEventArgs e)
    {
        if (_preview is null) return; // Fires while InitializeComponent is still filling the boxes.
        _preview.Name = NameBox.Text.Trim();
        _preview.Pronouns = Pronouns;
        _preview.Bio = BioBox.Text;
        BioCount.Text = $"{BioBox.Text.Length} / {BioBox.MaxLength}";
    }

    void PickAvatar(object sender, RoutedEventArgs e)
    {
        if (Pick("Choose a profile picture") is not { } file) return;
        try
        {
            _newAvatar = ProfileImages.PrepareAvatar(file);
            _removeAvatar = false;
            _preview.Avatar = ProfileImages.Decode(_newAvatar);
            ShowError(null);
        }
        catch (SupabaseException ex) { ShowError(ex.Message); }
        ShowButtons();
    }

    void PickBanner(object sender, RoutedEventArgs e)
    {
        if (Pick("Choose a banner image") is not { } file) return;
        try
        {
            _newBanner = ProfileImages.PrepareBanner(file);
            _removeBanner = false;
            _preview.Banner = ProfileImages.Decode(_newBanner);
            ShowError(null);
        }
        catch (SupabaseException ex) { ShowError(ex.Message); }
        ShowButtons();
    }

    void RemoveAvatar(object sender, RoutedEventArgs e)
    {
        _newAvatar = null;
        _removeAvatar = true;
        _preview.Avatar = null;
        ShowButtons();
    }

    void RemoveBanner(object sender, RoutedEventArgs e)
    {
        _newBanner = null;
        _removeBanner = true;
        _preview.Banner = null;
        ShowButtons();
    }

    string? Pick(string title)
    {
        var dialog = new OpenFileDialog { Title = title, Filter = ImageFilter, CheckFileExists = true };
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    void ShowButtons()
    {
        RemoveAvatarButton.Visibility = _preview.HasAvatar ? Visibility.Visible : Visibility.Collapsed;
        RemoveBannerButton.Visibility = _preview.HasBanner ? Visibility.Visible : Visibility.Collapsed;
    }

    async void Save(object sender, RoutedEventArgs e)
    {
        if (NameBox.Text.Trim().Length == 0) { ShowError("Enter the name people know you by."); return; }
        SaveButton.IsEnabled = false;
        SaveButton.Content = "Saving…";
        try
        {
            await _store.SaveAsync(NameBox.Text, Pronouns, BioBox.Text, _bannerColor, _newAvatar, _removeAvatar, _newBanner, _removeBanner);
            DialogResult = true;
        }
        catch (SupabaseException ex)
        {
            ShowError(ex.Message);
            SaveButton.IsEnabled = true;
            SaveButton.Content = "Save";
        }
    }

    void ShowError(string? message)
    {
        ErrorLine.Text = message ?? "";
        ErrorLine.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
    }
}
