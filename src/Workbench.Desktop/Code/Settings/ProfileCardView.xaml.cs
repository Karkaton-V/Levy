using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Media;

namespace Workbench.Desktop;

// Paints the parts of a profile card that bindings can't round off: the banner (image, or the chosen colour by resource key so it
// follows the theme) and the round picture.
public partial class ProfileCardView : UserControl
{
    ProfileCard? _card;

    public ProfileCardView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach(DataContext as ProfileCard);
    }

    void Attach(ProfileCard? card)
    {
        if (_card is not null) _card.PropertyChanged -= Changed;
        _card = card;
        if (card is not null) card.PropertyChanged += Changed;
        Paint();
    }

    void Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ProfileCard.Banner) or nameof(ProfileCard.BannerColor) or nameof(ProfileCard.Avatar)) Paint();
    }

    void Paint()
    {
        if (_card?.Banner is { } banner) BannerFill.Background = new ImageBrush(banner) { Stretch = Stretch.UniformToFill };
        else if (_card is not null) BannerFill.SetResourceReference(Border.BackgroundProperty, _card.BannerKey);
        else BannerFill.ClearValue(Border.BackgroundProperty);
        AvatarFill.Fill = _card?.Avatar is { } avatar ? new ImageBrush(avatar) { Stretch = Stretch.UniformToFill } : null;
    }
}
