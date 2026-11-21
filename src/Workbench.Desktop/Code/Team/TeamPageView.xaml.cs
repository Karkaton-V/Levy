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
}
