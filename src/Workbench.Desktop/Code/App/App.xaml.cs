using System.Windows;

namespace Workbench.Desktop;

public partial class App : Application
{
    SupabaseClient _client = null!;

    // App.xaml lives in Code/App, so windows are opened here rather than through StartupUri.
    // The saved session is restored when it can be; otherwise the app opens to the sign-in window.
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        ThemeManager.Initialize();
        _client = SupabaseClient.FromAssets();
        try
        {
            if (await _client.RestoreAsync()) await EnterAsync();
            else ShowSignIn();
        }
        catch (SupabaseException ex)
        {
            ShowSignIn(ex.Message);
        }
    }

    // Opens the user's company, or returns to sign-in with the reason they can't (waiting for review, declined, turned off).
    async Task EnterAsync()
    {
        string? problem;
        try
        {
            var (active, reason) = await Membership.CheckAsync(_client);
            if (active is not null) { ShowMain(active); return; }
            problem = reason;
        }
        catch (SupabaseException ex) { problem = ex.Message; }
        await _client.SignOutAsync();
        ShowSignIn(problem);
    }

    void ShowSignIn(string? message = null)
    {
        var window = new SignInWindow(_client, message);
        var signedIn = false;
        window.SignedIn += async () =>
        {
            signedIn = true;
            await EnterAsync();
            window.Close();
        };
        window.Closed += (_, _) => { if (!signedIn) Shutdown(); };
        MainWindow = window;
        window.Show();
    }

    void ShowMain(Membership membership)
    {
        var window = new MainWindow(_client, membership);
        var signingOut = false;
        window.SignOutRequested += async () =>
        {
            signingOut = true;
            await _client.SignOutAsync();
            ShowSignIn();
            window.Close();
        };
        window.Closed += (_, _) => { if (!signingOut) Shutdown(); };
        MainWindow = window;
        window.Show();
    }
}
