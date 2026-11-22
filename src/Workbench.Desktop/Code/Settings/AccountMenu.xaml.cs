using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Workbench.Desktop;

// Rail footer with the signed-in person: their name and picture open the profile card, the gear opens the settings menu. Each slides up
// from behind the footer; opening one closes the other first. The gear spins a turn as its menu opens and back as it closes.
public partial class AccountMenu : UserControl
{
    static readonly Duration Motion = TimeSpan.FromMilliseconds(380);
    // Far enough below the clip edge that a card is hidden before its first layout pass.
    const double Hidden = 2000;

    readonly Slider _settings;
    readonly Slider _profile;
    ProfileCard? _card;
    Window? _window;

    public AccountMenu()
    {
        InitializeComponent();
        _settings = new Slider(Menu, Card, CardSlide, Gear);
        _profile = new Slider(ProfilePopup, ProfileHost, ProfileSlide, ProfileButton);
        SignOutSection.Visibility = Visibility.Collapsed;
        foreach (var panel in new[] { _settings, _profile })
            panel.Content.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { Close(panel); panel.Toggle.Focus(); e.Handled = true; } };
        ShowPerson();
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
    }

    // The signed-in person's card: the footer, the menu header and the profile popup follow it.
    public ProfileCard? Profile
    {
        get => _card;
        set
        {
            if (_card is not null) _card.PropertyChanged -= CardChanged;
            _card = value;
            if (value is not null) value.PropertyChanged += CardChanged;
            ProfileView.DataContext = value;
            ShowPerson();
        }
    }

    public event Action? EditProfileRequested;
    public event Action? ChangePasswordRequested;
    public event Action? SignOutEverywhereRequested;

    // Raised by Sign out; the sign-out buttons only show while something is listening.
    public event Action? SignOutRequested
    {
        add { _signOut += value; SignOutSection.Visibility = Visibility.Visible; }
        remove { _signOut -= value; SignOutSection.Visibility = _signOut is null ? Visibility.Collapsed : Visibility.Visible; }
    }
    Action? _signOut;

    // A slide-up card: its popup, the element that slides, its transform, and the footer toggle that opens it.
    sealed record Slider(Popup Popup, FrameworkElement Content, TranslateTransform Slide, ToggleButton Toggle)
    {
        public bool IsOpen { get; set; }
        public double Travel => Content.ActualHeight + Content.Margin.Top + Content.Margin.Bottom;
    }

    // Clicks elsewhere in the window, switching apps, or moving the window all close whichever card is open.
    void Attach()
    {
        ThemeManager.Changed += ShowTheme;
        _settings.Slide.Y = _profile.Slide.Y = Hidden;
        _window = Window.GetWindow(this);
        if (_window is null) return;
        _window.PreviewMouseDown += WindowMouseDown;
        _window.Deactivated += CloseOnDeactivate;
        _window.LocationChanged += CloseNow;
        _window.SizeChanged += CloseNow;
    }

    void Detach()
    {
        ThemeManager.Changed -= ShowTheme;
        if (_window is null) return;
        _window.PreviewMouseDown -= WindowMouseDown;
        _window.Deactivated -= CloseOnDeactivate;
        _window.LocationChanged -= CloseNow;
        _window.SizeChanged -= CloseNow;
        _window = null;
    }

    void WindowMouseDown(object sender, MouseButtonEventArgs e)
    {
        foreach (var panel in new[] { _settings, _profile })
            if (panel.IsOpen && !panel.Content.IsMouseOver && !panel.Toggle.IsMouseOver) Close(panel);
    }

    void CloseOnDeactivate(object? sender, EventArgs e) { Close(_settings); Close(_profile); }

    void CloseNow(object? sender, EventArgs e)
    {
        foreach (var panel in new[] { _settings, _profile })
            if (panel.IsOpen) { Close(panel); Finish(panel); }
    }

    void GearClicked(object sender, RoutedEventArgs e) => Toggle(_settings, _profile);
    void ProfileClicked(object sender, RoutedEventArgs e) => Toggle(_profile, _settings);

    void Toggle(Slider panel, Slider other)
    {
        if (panel.IsOpen) { Close(panel); return; }
        if (other.IsOpen) { Close(other); Finish(other); }
        Open(panel);
    }

    static bool ReduceMotion => AppSettings.Load().ReduceMotion;
    static Duration Speed => ReduceMotion ? TimeSpan.FromMilliseconds(1) : Motion;

    void Open(Slider panel)
    {
        panel.IsOpen = true;
        panel.Toggle.IsChecked = true;
        ShowTheme();
        panel.Popup.IsOpen = true;
        if (panel == _settings) Spin(360);
        // The card's height is only known after the popup lays it out; start the slide then.
        Dispatcher.InvokeAsync(() =>
        {
            if (!panel.IsOpen) return;
            Slide(panel, Math.Min(panel.Slide.Y, panel.Travel), 0, null);
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    void Close(Slider panel)
    {
        if (!panel.IsOpen) return;
        panel.IsOpen = false;
        panel.Toggle.IsChecked = false;
        if (panel == _settings) Spin(0);
        Slide(panel, panel.Slide.Y, panel.Travel, () => Finish(panel));
    }

    // Hides the popup once the card is out of sight, unless it was reopened meanwhile.
    void Finish(Slider panel)
    {
        if (panel.IsOpen) return;
        panel.Slide.BeginAnimation(TranslateTransform.YProperty, null);
        panel.Slide.Y = Hidden;
        if (panel == _settings)
        {
            GearSpin.BeginAnimation(RotateTransform.AngleProperty, null);
            GearSpin.Angle = 0;
        }
        panel.Popup.IsOpen = false;
    }

    void Spin(double to) =>
        GearSpin.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(to, Speed) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } });

    static void Slide(Slider panel, double from, double to, Action? done)
    {
        var animation = new DoubleAnimation(from, to, Speed) { EasingFunction = new CubicEase { EasingMode = to == 0 ? EasingMode.EaseOut : EasingMode.EaseIn } };
        if (done is not null) animation.Completed += (_, _) => done();
        panel.Slide.BeginAnimation(TranslateTransform.YProperty, animation);
    }

    void CardChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ProfileCard.Name) or nameof(ProfileCard.Email) or nameof(ProfileCard.Avatar)) ShowPerson();
    }

    // The footer and the menu header: name, email, and the picture (or initials until there is one).
    void ShowPerson()
    {
        var name = string.IsNullOrWhiteSpace(_card?.Name) ? "Guest" : _card!.Name.Trim();
        var email = string.IsNullOrWhiteSpace(_card?.Email) ? "Not signed in" : _card!.Email.Trim();
        FooterName.Text = MenuName.Text = name;
        FooterEmail.Text = MenuEmail.Text = email;
        var picture = _card?.Avatar is { } avatar ? new ImageBrush(avatar) { Stretch = Stretch.UniformToFill } : null;
        FooterPicture.Fill = picture;
        MenuPicture.Fill = picture;
        // With a picture, a black circle sits behind it instead of the initials.
        FooterInitials.Text = MenuInitials.Text = picture is null ? ProfileCard.InitialsOf(name) : "";
        foreach (var circle in new[] { FooterAvatar, MenuAvatar })
            if (picture is null) circle.ClearValue(Border.BackgroundProperty);
            else circle.SetResourceReference(Border.BackgroundProperty, "AvatarBackdropFill");
    }

    void ShowTheme()
    {
        LightOption.IsChecked = !ThemeManager.IsDark;
        DarkOption.IsChecked = ThemeManager.IsDark;
        ReduceMotionOption.IsChecked = ReduceMotion;
    }

    void ThemeChosen(object sender, RoutedEventArgs e)
    {
        var theme = sender == DarkOption ? AppTheme.Dark : AppTheme.Light;
        if (theme != ThemeManager.Current) ThemeManager.Apply(theme);
    }

    void ReduceMotionClicked(object sender, RoutedEventArgs e) => (AppSettings.Load() with { ReduceMotion = ReduceMotionOption.IsChecked == true }).Save();

    // Menu actions close the cards first, so the window that opens isn't covered.
    void Run(Action? action)
    {
        Close(_settings);
        Close(_profile);
        action?.Invoke();
    }

    void EditProfileClicked(object sender, RoutedEventArgs e) => Run(EditProfileRequested);
    void ChangePasswordClicked(object sender, RoutedEventArgs e) => Run(ChangePasswordRequested);
    void SignOutClicked(object sender, RoutedEventArgs e) => Run(_signOut);
    void SignOutEverywhereClicked(object sender, RoutedEventArgs e) => Run(SignOutEverywhereRequested);
}
