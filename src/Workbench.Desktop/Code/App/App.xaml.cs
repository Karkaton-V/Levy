using System.Windows;

namespace Workbench.Desktop;

public partial class App : Application
{
    // App.xaml lives in Code/App, so the window is opened here rather than through StartupUri.
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        new MainWindow().Show();
    }
}
