using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Navigation;

namespace Workbench.Desktop;

public partial class IntakePageView : UserControl
{
    public IntakePageView() => InitializeComponent();

    // Opens the GitLab issue: in the Mock GitLab window in demo mode, otherwise in the browser (http(s) links only).
    void Link_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        e.Handled = true;
        if (DataContext is WorkspaceViewModel workspace && workspace.OpenInMock(e.Uri)) return;
        if (e.Uri is { Scheme: "https" or "http" }) Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
    }
}
