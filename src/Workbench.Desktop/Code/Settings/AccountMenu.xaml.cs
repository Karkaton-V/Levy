using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Workbench.Desktop;

// Rail footer with the signed-in user, and the settings menu (account, Light/Dark switch, Sign out) that opens upward.
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

    public AccountMenu()
    {
        InitializeComponent();
        SignOutSection.Visibility = Visibility.Collapsed;
        ShowUser();
        // While the menu is open the gear ignores clicks: a click outside closes the menu, and must not reopen it straight away.
        Menu.Opened += (_, _) => { Gear.IsHitTestVisible = false; ShowTheme(); };
        Menu.Closed += (_, _) => Gear.IsHitTestVisible = true;
        Menu.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { Menu.IsOpen = false; Gear.Focus(); e.Handled = true; } };
        Loaded += (_, _) => ThemeManager.Changed += ShowTheme;
        Unloaded += (_, _) => ThemeManager.Changed -= ShowTheme;
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
        Menu.IsOpen = false;
        _signOut?.Invoke();
    }
}
