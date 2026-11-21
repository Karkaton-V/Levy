using System.Windows;

namespace Workbench.Desktop;

// Sign in, or create an account (name, optional company code, email, password). Raises SignedIn once there is a session.
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
        if (SubmitText is null) return; // Fires once while InitializeComponent is still building the form; SubmitText is its last named element.
        NameSection.Visibility = Creating ? Visibility.Visible : Visibility.Collapsed;
        Heading.Text = Creating ? "Create your account" : "Welcome back";
        Subheading.Text = Creating ? "Join your company with its code, or leave it blank for an office of your own." : "Sign in to your workspace.";
        SubmitText.Text = Creating ? "Create account" : "Sign in";
        ShowError(null);
        (Creating ? NameBox : EmailBox).Focus();
    }

    async void Submit(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        var email = EmailBox.Text.Trim();
        var password = PasswordBox.Password;
        var code = CompanyBox.Text.Trim();
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
                // Check the code first, so a typo doesn't make an account that belongs nowhere.
                if (code.Length > 0 && await _client.CompanyForCodeAsync(code) is null)
                {
                    ShowError($"No company uses the code {code}. Check it with your company's admin.");
                    return;
                }
                if (!await _client.SignUpAsync(name, email, password, code))
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

    CancellationTokenSource? _gitLab;

    // Opens GitLab's sign-in in the browser and waits for it; pressing the button again cancels the wait.
    async void GitLab_Click(object sender, RoutedEventArgs e)
    {
        if (_gitLab is not null) { _gitLab.Cancel(); return; }
        _gitLab = new CancellationTokenSource();
        SubmitButton.IsEnabled = false;
        GitLabText.Text = "Cancel GitLab sign-in";
        ShowInfo("Finish signing in to GitLab in your browser. Levy will continue on its own.");
        try
        {
            await _client.SignInWithGitLabAsync(GitLabSettings.FromAssets(), _gitLab.Token);
            SignedIn?.Invoke();
        }
        catch (OperationCanceledException) { ShowInfo("GitLab sign-in cancelled."); }
        catch (SupabaseException ex) { ShowError(ex.Message); }
        finally
        {
            _gitLab.Dispose();
            _gitLab = null;
            SubmitButton.IsEnabled = true;
            GitLabText.Text = "Sign in with GitLab";
        }
    }

    void SetBusy(bool busy)
    {
        GitLabButton.IsEnabled = !busy;
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
