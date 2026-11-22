using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Workbench.Desktop;

// Paints the parts of a profile card that bindings can't round off (the banner and the round picture), and cuts the bio to three lines
// with "View Full Bio" when it's longer.
public partial class ProfileCardView : UserControl
{
    const int BioLines = 3;

    ProfileCard? _card;
    bool _bioOpen;

    public ProfileCardView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach(DataContext as ProfileCard);
        BioText.SizeChanged += (_, e) => { if (e.WidthChanged) FitBio(); };
    }

    void Attach(ProfileCard? card)
    {
        if (_card is not null) _card.PropertyChanged -= Changed;
        _card = card;
        if (card is not null) card.PropertyChanged += Changed;
        _bioOpen = false;
        Paint();
        FitBio();
    }

    void Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ProfileCard.Banner) or nameof(ProfileCard.BannerColor) or nameof(ProfileCard.Avatar)) Paint();
        if (e.PropertyName == nameof(ProfileCard.Bio)) Dispatcher.InvokeAsync(FitBio);
    }

    void Paint()
    {
        if (_card?.Banner is { } banner) BannerFill.Background = new ImageBrush(banner) { Stretch = Stretch.UniformToFill };
        else if (_card is not null) BannerFill.SetResourceReference(Border.BackgroundProperty, _card.BannerKey);
        else BannerFill.ClearValue(Border.BackgroundProperty);
        AvatarFill.Fill = _card?.Avatar is { } avatar ? new ImageBrush(avatar) { Stretch = Stretch.UniformToFill } : null;
    }

    // Shows three lines unless the bio is open; offers the toggle only when the whole bio needs more than three.
    void FitBio()
    {
        var shortHeight = BioText.LineHeight * BioLines;
        var width = BioText.ActualWidth;
        if (width <= 0 || string.IsNullOrEmpty(BioText.Text))
        {
            BioToggle.Visibility = Visibility.Collapsed;
            return;
        }
        var whole = new TextBlock { Text = BioText.Text, TextWrapping = TextWrapping.Wrap, FontFamily = BioText.FontFamily, FontSize = BioText.FontSize,
            LineHeight = BioText.LineHeight, LineStackingStrategy = BioText.LineStackingStrategy };
        whole.Measure(new Size(width, double.PositiveInfinity));
        var longer = whole.DesiredSize.Height > shortHeight + 1;
        BioToggle.Visibility = longer ? Visibility.Visible : Visibility.Collapsed;
        BioText.MaxHeight = longer && !_bioOpen ? shortHeight : double.PositiveInfinity;
        BioToggle.Content = _bioOpen ? "Show Less" : "View Full Bio";
    }

    void BioToggle_Click(object sender, RoutedEventArgs e)
    {
        _bioOpen = !_bioOpen;
        FitBio();
    }
}
