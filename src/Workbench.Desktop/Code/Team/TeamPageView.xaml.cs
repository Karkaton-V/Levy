using System.Windows;
using System.Windows.Controls;

namespace Workbench.Desktop;

public partial class TeamPageView : UserControl
{
    public TeamPageView() => InitializeComponent();

    void CopyCode_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is WorkspaceViewModel { JoinCode.Length: > 0 } workspace) Clipboard.SetText(workspace.JoinCode);
    }

    // Deleting an account can't be undone, so it asks first, naming the person and what stays.
    async void DeleteAccount_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not WorkspaceViewModel { Team.Draft: { } person } workspace) return;
        var answer = MessageBox.Show(Window.GetWindow(this),
            $"Delete {person.Name}'s account?\n\nThis permanently deletes their sign-in and profile ({person.Email}). " +
            "Their jobs, tasks and records stay with the company, unassigned. This can't be undone.",
            "Delete account", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;
        if (await workspace.DeleteDraftAccountAsync())
            MessageBox.Show(Window.GetWindow(this), $"{person.Name}'s account was deleted.", "Delete account", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
