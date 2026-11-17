using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace Workbench.Desktop;

// Rail footer with the signed-in user, and the settings menu (account, Light/Dark switch, Sign out) that rises from behind it.
// Opening spins the gear one turn clockwise and slides the card up; closing spins it back and slides the card down.
public partial class AccountMenu : UserControl
{
    public static readonly DependencyProperty UserNameProperty =
        DependencyProperty.Register(nameof(UserName), typeof(string), typeof(AccountMenu), new PropertyMetadata("", (d, _) => ((AccountMenu)d).ShowUser()));
    public static readonly DependencyProperty EmailProperty =
        DependencyProperty.Register(nameof(Email), typeof(string), typeof(AccountMenu), new PropertyMetadata("", (d, _) => ((AccountMenu)d).ShowUser()));

    public string UserName { get => (string)GetValue(UserNameProperty); set => SetValue(UserNameProperty, value); }
    public string Email { get => (string)GetValue(EmailProperty); set => SetValue(EmailProperty, value); }

    // Raised by the Sign out button; the button only shows while something is listening.
    public event Action? SignOutRequested
    {
        add { _signOut += value; SignOutSection.Visibility = Visibility.Visible; }
        remove { _signOut -= value; SignOutSection.Visibility = _signOut is null ? Visibility.Collapsed : Visibility.Visible; }
    }
    Action? _signOut;

    static readonly Duration Motion = TimeSpan.FromMilliseconds(380);
    // Far enough below the clip edge that the card is hidden before its first layout pass.
    const double Hidden = 2000;

    Window? _window;
    bool _isOpen;

    public AccountMenu()
    {
        InitializeComponent();
        SignOutSection.Visibility = Visibility.Collapsed;
        CardSlide.Y = Hidden;
        ShowUser();
        Card.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { Close(); Gear.Focus(); e.Handled = true; } };
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
    }

    // Clicks elsewhere in the window, switching apps, or moving the window all close the menu.
    void Attach()
    {
        ThemeManager.Changed += ShowTheme;
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
        if (_isOpen && !Card.IsMouseOver && !Gear.IsMouseOver) Close();
    }

    void CloseOnDeactivate(object? sender, EventArgs e) => Close();
    void CloseNow(object? sender, EventArgs e) { if (_isOpen) { Close(); Finish(); } }

    void GearClicked(object sender, RoutedEventArgs e)
    {
        if (_isOpen) Close();
        else Open();
    }

    void Open()
    {
        _isOpen = true;
        Gear.IsChecked = true;
        ShowTheme();
        Menu.IsOpen = true;
        Spin(360);
        // The card's height is only known after the popup lays it out; start the slide then.
        Dispatcher.InvokeAsync(() =>
        {
            if (!_isOpen) return;
            var from = Math.Min(CardSlide.Y, Travel());
            Slide(from, 0, null);
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    void Close()
    {
        if (!_isOpen) return;
        _isOpen = false;
        Gear.IsChecked = false;
        Spin(0);
        Slide(CardSlide.Y, Travel(), Finish);
    }

    // Hides the popup once the card is out of sight, unless it was reopened meanwhile.
    void Finish()
    {
        if (_isOpen) return;
        CardSlide.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, null);
        CardSlide.Y = Hidden;
        GearSpin.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, null);
        GearSpin.Angle = 0;
        Menu.IsOpen = false;
    }

    // Distance from fully shown to just below the popup's bottom edge.
    double Travel() => Card.ActualHeight + Card.Margin.Top + Card.Margin.Bottom;

    void Spin(double to) =>
        GearSpin.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty,
            new DoubleAnimation(to, Motion) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } });

    void Slide(double from, double to, Action? done)
    {
        var animation = new DoubleAnimation(from, to, Motion)
        {
            EasingFunction = new CubicEase { EasingMode = to == 0 ? EasingMode.EaseOut : EasingMode.EaseIn },
        };
        if (done is not null) animation.Completed += (_, _) => done();
        CardSlide.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, animation);
    }

    void ShowUser()
    {
        var name = string.IsNullOrWhiteSpace(UserName) ? "Guest" : UserName.Trim();
        var email = string.IsNullOrWhiteSpace(Email) ? "Not signed in" : Email.Trim();
        FooterName.Text = MenuName.Text = name;
        FooterEmail.Text = MenuEmail.Text = email;
        FooterInitials.Text = MenuInitials.Text = Initials(name);
    }

    // "Ada Lovelace" → "AL", "ada" → "A".
    static string Initials(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => "?",
            1 => char.ToUpperInvariant(parts[0][0]).ToString(),
            _ => $"{char.ToUpperInvariant(parts[0][0])}{char.ToUpperInvariant(parts[^1][0])}",
        };
    }

    void ShowTheme()
    {
        LightOption.IsChecked = !ThemeManager.IsDark;
        DarkOption.IsChecked = ThemeManager.IsDark;
    }

    void ThemeChosen(object sender, RoutedEventArgs e)
    {
        var theme = sender == DarkOption ? AppTheme.Dark : AppTheme.Light;
        if (theme != ThemeManager.Current) ThemeManager.Apply(theme);
    }

    void SignOutClicked(object sender, RoutedEventArgs e)
    {
        Close();
        _signOut?.Invoke();
    }
}
