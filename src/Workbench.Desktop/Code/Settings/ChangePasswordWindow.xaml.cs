using System.Windows;

namespace Workbench.Desktop;

// Sets a new password for the signed-in account (Supabase Auth checks it too, e.g. against leaked-password lists when that's on).
public partial class ChangePasswordWindow : Window
{
    const int Minimum = 8;
    readonly SupabaseClient _client;

    public ChangePasswordWindow(SupabaseClient client)
    {
        InitializeComponent();
        _client = client;
        Loaded += (_, _) => NewBox.Focus();
    }

    async void Save(object sender, RoutedEventArgs e)
    {
        var problem = NewBox.Password.Length < Minimum ? $"Use at least {Minimum} characters."
            : NewBox.Password != RepeatBox.Password ? "The two passwords don't match."
            : null;
        if (problem is not null) { ShowError(problem); return; }
        SaveButton.IsEnabled = false;
        try
        {
            await _client.ChangePasswordAsync(NewBox.Password);
            DialogResult = true;
        }
        catch (SupabaseException ex)
        {
            ShowError(ex.Message);
            SaveButton.IsEnabled = true;
        }
    }

    void ShowError(string message)
    {
        ErrorLine.Text = message;
        ErrorLine.Visibility = Visibility.Visible;
    }
}
