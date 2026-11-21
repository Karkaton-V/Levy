using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Navigation;

namespace Workbench.Desktop;

public partial class IntakePageView : UserControl
{
    public IntakePageView() => InitializeComponent();

    // Opens the GitLab issue in the browser; only http(s) links are followed.
    void Link_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        if (e.Uri is { Scheme: "https" or "http" }) Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
