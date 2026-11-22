using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace Workbench.Desktop;

// Edits the signed-in person's profile. Changes show in the preview card as they're made and are saved together (text, banner colour,
// new or removed pictures); Cancel, the close button or Esc leaves everything as it was. Shown inside the main window (MainWindow.OpenDialog).
public partial class EditProfileDialog : UserControl
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
    // The full images picked in this dialog and the areas chosen from them, so Adjust can reopen the cropper.
    BitmapSource? _avatarSource, _bannerSource;
    Int32Rect _avatarArea, _bannerArea;
    // While cropping: which image, its full source, and what the preview showed before (restored by Back).
    bool _croppingBanner;
    BitmapSource? _cropSource;
    ImageSource? _beforeCrop;

    public event Action? Closed;

    public EditProfileDialog(ProfileStore store)
    {
        InitializeComponent();
        _store = store;
        _preview = store.Card.CopyForPreview();
        _bannerColor = store.Card.BannerColor;
        PreviewCard.DataContext = _preview;
        NameBox.Text = store.Card.Name;
        PronounsBox.ItemsSource = ProfileCard.PronounChoices;
        PronounsBox.SelectedItem = ProfileCard.PronounChoices.FirstOrDefault(p => p == store.Card.Pronouns);
        BioBox.Text = store.Card.Bio;
        foreach (var color in BannerColors)
        {
            var swatch = new RadioButton { Style = (Style)FindResource("BannerSwatch"), GroupName = "Banner", IsChecked = color == _bannerColor, Tag = color, ToolTip = char.ToUpperInvariant(color[0]) + color[1..] };
            swatch.SetResourceReference(BackgroundProperty, "Banner" + char.ToUpperInvariant(color[0]) + color[1..]);
            swatch.Checked += (_, _) => { _bannerColor = (string)swatch.Tag; _preview.BannerColor = _bannerColor; };
            Swatches.Children.Add(swatch);
        }
        Cropper.Changed += CropMoved;
        // A sample from the starting rules: a comment, two land rates and a building cost.
        var lines = Shared.ReadAsset("Editor", "DefaultRules.rules").Split('\n').Select(l => l.TrimEnd()).Where(l => l.Length > 0).ToList();
        CodeSample.Text = string.Join("\n", new[] { 0, 1, 2, 4 }.Where(i => i < lines.Count).Select(i => lines[i]));
        CodeThemeBox.ItemsSource = CodeThemeInfo.Catalog;
        CodeThemeBox.SelectedItem = CodeThemeInfo.Catalog.FirstOrDefault(t => t.Key == store.CodeTheme) ?? CodeThemeInfo.Catalog[0];
        Loaded += (_, _) => ThemeManager.Changed += PaintSample;
        Unloaded += (_, _) => ThemeManager.Changed -= PaintSample;
        ShowButtons();
        Loaded += (_, _) => NameBox.Focus();
    }

    bool Cropping => CropPanel.Visibility == Visibility.Visible;

    // Esc cancels; Enter in a one-line box saves, like a dialog window's default button.
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { if (Cropping) CancelCrop(this, e); else Cancel(this, e); e.Handled = true; }
        else if (e.Key == Key.Enter && Cropping) { ApplyCrop(this, e); e.Handled = true; }
        else if (e.Key == Key.Enter && e.OriginalSource is TextBox { AcceptsReturn: false }) { Save(this, e); e.Handled = true; }
        base.OnPreviewKeyDown(e);
    }

    void Cancel(object sender, RoutedEventArgs e) { if (SaveButton.IsEnabled) Closed?.Invoke(); }

    string Pronouns => PronounsBox.SelectedItem as string ?? "";
    string CodeTheme => (CodeThemeBox.SelectedItem as CodeThemeInfo)?.Key ?? "app";

    void CodeThemeChanged(object sender, SelectionChangedEventArgs e) => PaintSample();
    void PaintSample() => Shared.PaintCode(CodeSample, CodeTheme);

    void Preview(object sender, RoutedEventArgs e)
    {
        if (_preview is null) return; // Fires while InitializeComponent is still filling the boxes.
        _preview.Name = NameBox.Text.Trim();
        _preview.Pronouns = Pronouns;
        _preview.Bio = BioBox.Text;
        BioCount.Text = $"{BioBox.Text.Length} / {BioBox.MaxLength}";
    }

    void PickAvatar(object sender, RoutedEventArgs e) => PickAndCrop("Choose a profile picture", banner: false);
    void PickBanner(object sender, RoutedEventArgs e) => PickAndCrop("Choose a banner image", banner: true);
    void AdjustAvatar(object sender, RoutedEventArgs e) { if (_avatarSource is { } source) StartCrop(source, banner: false, _avatarArea); }
    void AdjustBanner(object sender, RoutedEventArgs e) { if (_bannerSource is { } source) StartCrop(source, banner: true, _bannerArea); }

    void PickAndCrop(string title, bool banner)
    {
        if (Pick(title) is not { } file) return;
        try
        {
            StartCrop(ProfileImages.Load(file), banner, null);
            ShowError(null);
        }
        catch (SupabaseException ex) { ShowError(ex.Message); }
    }

    // Opens the cropper over the form: square and round for a picture, 3:1 for a banner.
    void StartCrop(BitmapSource source, bool banner, Int32Rect? area)
    {
        _croppingBanner = banner;
        _cropSource = source;
        _beforeCrop = banner ? _preview.Banner : _preview.Avatar;
        CropTitle.Text = banner ? "Adjust banner" : "Adjust picture";
        CropPanel.Visibility = Visibility.Visible;
        Cropper.Load(source, banner ? ProfileImages.BannerAspect : ProfileImages.AvatarAspect, round: !banner, area);
    }

    // The preview shows the framed area as it moves.
    void CropMoved()
    {
        if (_cropSource is null || !Cropping) return;
        var part = new CroppedBitmap(_cropSource, Cropper.Area);
        part.Freeze();
        if (_croppingBanner) _preview.Banner = part; else _preview.Avatar = part;
    }

    void ApplyCrop(object sender, RoutedEventArgs e)
    {
        if (_cropSource is not { } source) return;
        var area = Cropper.Area;
        if (_croppingBanner)
        {
            (_bannerSource, _bannerArea, _removeBanner) = (source, area, false);
            _newBanner = ProfileImages.PrepareBanner(source, area);
            _preview.Banner = ProfileImages.Decode(_newBanner);
        }
        else
        {
            (_avatarSource, _avatarArea, _removeAvatar) = (source, area, false);
            _newAvatar = ProfileImages.PrepareAvatar(source, area);
            _preview.Avatar = ProfileImages.Decode(_newAvatar);
        }
        EndCrop();
    }

    void CancelCrop(object sender, RoutedEventArgs e)
    {
        if (_croppingBanner) _preview.Banner = _beforeCrop; else _preview.Avatar = _beforeCrop;
        EndCrop();
    }

    void EndCrop()
    {
        _cropSource = null;
        CropPanel.Visibility = Visibility.Collapsed;
        ShowButtons();
        NameBox.Focus(); // The cropper had focus; without this, Esc and Enter no longer reach the dialog.
    }

    void RemoveAvatar(object sender, RoutedEventArgs e)
    {
        _newAvatar = null;
        _avatarSource = null;
        _removeAvatar = true;
        _preview.Avatar = null;
        ShowButtons();
    }

    void RemoveBanner(object sender, RoutedEventArgs e)
    {
        _newBanner = null;
        _bannerSource = null;
        _removeBanner = true;
        _preview.Banner = null;
        ShowButtons();
    }

    string? Pick(string title)
    {
        var dialog = new OpenFileDialog { Title = title, Filter = ImageFilter, CheckFileExists = true };
        return dialog.ShowDialog(Window.GetWindow(this)) == true ? dialog.FileName : null;
    }

    void ShowButtons()
    {
        RemoveAvatarButton.Visibility = _preview.HasAvatar ? Visibility.Visible : Visibility.Collapsed;
        RemoveBannerButton.Visibility = _preview.HasBanner ? Visibility.Visible : Visibility.Collapsed;
        AdjustAvatarButton.Visibility = _avatarSource is null ? Visibility.Collapsed : Visibility.Visible;
        AdjustBannerButton.Visibility = _bannerSource is null ? Visibility.Collapsed : Visibility.Visible;
    }

    async void Save(object sender, RoutedEventArgs e)
    {
        if (!SaveButton.IsEnabled || Cropping) return;
        if (NameBox.Text.Trim().Length == 0) { ShowError("Enter the name people know you by."); return; }
        SaveButton.IsEnabled = false;
        SaveButton.Content = "Saving…";
        try
        {
            await _store.SaveAsync(NameBox.Text, Pronouns, BioBox.Text, _bannerColor, CodeTheme, _newAvatar, _removeAvatar, _newBanner, _removeBanner);
            Closed?.Invoke();
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
