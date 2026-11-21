using System.Windows;

namespace Workbench.Desktop;

public partial class MockGitLabWindow : Window
{
    readonly MockGitLabViewModel _viewModel;

    public MockGitLabWindow(MockGitLabViewModel viewModel)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;
        Closed += (_, _) => viewModel.Detach();
    }

    public void Show(string project, int iid)
    {
        _viewModel.Select(project, iid);
        Show();
        Activate();
    }
}
