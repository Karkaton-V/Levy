using System.Windows;

namespace Workbench.Desktop;

// Sign in, or create an account (name, email, password). Raises SignedIn once there is a session.
public partial class SignInWindow : Window
{
    const int MinimumPassword = 6;
    readonly SupabaseClient _client;

    public event Action? SignedIn;

    public SignInWindow(SupabaseClient client, string? message = null)
    {
        InitializeComponent();
        _client = client;
        if (message is not null) ShowError(message);
        Loaded += (_, _) => EmailBox.Focus();
    }

    bool Creating => CreateMode.IsChecked == true;

    void ModeChanged(object sender, RoutedEventArgs e)
    {
        if (Heading is null) return; // Fires once during InitializeComponent, before the rest of the form exists.
        NameSection.Visibility = Creating ? Visibility.Visible : Visibility.Collapsed;
        Heading.Text = Creating ? "Create your account" : "Welcome back";
        Subheading.Text = Creating ? "You get a workspace of your own, with sample work to explore." : "Sign in to your workspace.";
        SubmitText.Text = Creating ? "Create account" : "Sign in";
        ShowError(null);
        (Creating ? NameBox : EmailBox).Focus();
    }

    async void Submit(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        var email = EmailBox.Text.Trim();
        var password = PasswordBox.Password;
        var problem = Creating && name.Length == 0 ? "Enter your name."
            : !email.Contains('@') ? "Enter your email address."
            : password.Length == 0 ? "Enter your password."
            : Creating && password.Length < MinimumPassword ? $"Use a password of at least {MinimumPassword} characters."
            : null;
        if (problem is not null)
        {
            ShowError(problem);
            return;
        }

        SetBusy(true);
        try
        {
            if (Creating)
            {
                if (!await _client.SignUpAsync(name, email, password))
                {
                    // Email confirmation is on: no session until the link is clicked.
                    SignInMode.IsChecked = true;
                    PasswordBox.Clear();
                    ShowInfo($"Check {email} for a link to confirm your account, then sign in here.");
                    return;
                }
            }
            else await _client.SignInAsync(email, password);
            SignedIn?.Invoke();
        }
        catch (SupabaseException ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    void SetBusy(bool busy)
    {
        SubmitButton.IsEnabled = !busy;
        SubmitText.Text = busy ? (Creating ? "Creating account…" : "Signing in…") : Creating ? "Create account" : "Sign in";
        Cursor = busy ? System.Windows.Input.Cursors.Wait : null;
    }

    void ShowError(string? message)
    {
        ErrorLine.Text = message ?? "";
        ErrorLine.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
        InfoLine.Visibility = Visibility.Collapsed;
    }

    void ShowInfo(string message)
    {
        InfoLine.Text = message;
        InfoLine.Visibility = Visibility.Visible;
        ErrorLine.Visibility = Visibility.Collapsed;
    }
}
