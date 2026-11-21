using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;

namespace Workbench.Desktop;

public partial class MainWindow : Window
{
    // Raised by Sign out in the account menu; the app closes this window and returns to sign-in.
    public event Action? SignOutRequested;

    readonly MainViewModel _viewModel;
    readonly Dictionary<ListBoxItem, TabItem> _pages;
    readonly HashSet<TabItem> _workflowPages;
    readonly WorkspaceViewModel _workspace;

    public MainWindow(SupabaseClient client, Membership membership)
    {
        InitializeComponent();
        FitToScreen();
        Account.UserName = client.User?.FullName ?? "";
        Account.Email = client.User?.Email ?? "";
        Account.SignOutRequested += () => SignOutRequested?.Invoke();
        // The starting rules and the study area are assets, not strings in code.
        var viewModel = _viewModel = new MainViewModel(Shared.ReadAsset("Editor", "DefaultRules.rules"), StudyArea.Default);
        DataContext = viewModel;
        viewModel.OpenInEditorRequested += OpenInEditor;

        // The workflow pages read and save the organization's rows in Supabase.
        var workspace = _workspace = new WorkspaceViewModel(client, membership);
        foreach (var view in new FrameworkElement[] { JobsView, ProjectsView, BoardView, TasksView, ClientsView, ReportsView, RequestsView, IntakeView, TeamView, ActivityView, CompaniesView }) view.DataContext = workspace;
        viewModel.WorkspaceName = workspace.OrganizationName;
        JobTitleText.SetBinding(TextBlock.TextProperty, new Binding(nameof(WorkspaceViewModel.JobTitle)) { Source = workspace });
        ShowAdminPages(workspace);
        workspace.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(WorkspaceViewModel.OrganizationName)) viewModel.WorkspaceName = workspace.OrganizationName;
            if (e.PropertyName == nameof(WorkspaceViewModel.IsAdmin)) ShowAdminPages(workspace);
        };

        _pages = new()
        {
            [NavRules] = RulesPage, [NavMap] = MapPage, [NavCompare] = ComparePage,
            [NavJobs] = JobsPage, [NavProjects] = ProjectsPage, [NavBoard] = BoardPage,
            [NavTasks] = TasksPage, [NavClients] = ClientsPage, [NavReports] = ReportsPage,
            [NavRequests] = RequestsPage, [NavIntake] = IntakePage,
            [NavTeam] = TeamPage, [NavActivity] = ActivityPage, [NavCompanies] = CompaniesPage,
        };
        _workflowPages = [JobsPage, ProjectsPage, BoardPage, TasksPage, ClientsPage, ReportsPage, RequestsPage, IntakePage, TeamPage, ActivityPage, CompaniesPage];
        workspace.ContextSource = DescribeContext;
        workspace.MockGitLabRequested += OpenMockGitLab;
        Tabs.SelectedItem = RulesPage;

        Loaded += async (_, _) => await Task.WhenAll(viewModel.LoadBuildingsAsync(), workspace.LoadAsync());
    }

    // With display scaling (125%, 150%) the screen can be smaller than the default size; shrink to fit and centre in the space above the taskbar.
    void FitToScreen()
    {
        var area = SystemParameters.WorkArea;
        MinWidth = Math.Min(MinWidth, area.Width);
        MinHeight = Math.Min(MinHeight, area.Height);
        Width = Math.Min(Width, area.Width * 0.95);
        Height = Math.Min(Height, area.Height * 0.95);
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = area.Left + (area.Width - Width) / 2;
        Top = area.Top + (area.Height - Height) / 2;
    }

    // The window's frame and state before going full screen, restored when leaving it.
    WindowState _stateBeforeFullScreen;
    WindowStyle _styleBeforeFullScreen;
    ResizeMode _resizeBeforeFullScreen;
    bool IsFullScreen => WindowStyle == WindowStyle.None;

    // F11 toggles full screen from anywhere, including while typing in the code view.
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.F11)
        {
            ToggleFullScreen();
            e.Handled = true;
        }
        base.OnPreviewKeyDown(e);
    }

    // Esc also leaves full screen, but only if nothing else (search box, autocomplete) used the key first.
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && IsFullScreen && !e.Handled)
        {
            ToggleFullScreen();
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    void ToggleFullScreen()
    {
        if (IsFullScreen)
        {
            WindowStyle = _styleBeforeFullScreen;
            ResizeMode = _resizeBeforeFullScreen;
            WindowState = _stateBeforeFullScreen;
            return;
        }
        _stateBeforeFullScreen = WindowState;
        _styleBeforeFullScreen = WindowStyle;
        _resizeBeforeFullScreen = ResizeMode;
        // Drop the frame first, then maximize from Normal so the window covers the taskbar too.
        WindowState = WindowState.Normal;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowState = WindowState.Maximized;
    }

    // The ADMIN section: Team for company admins (and platform admins), Activity for company admins, Intake for account managers, Companies for platform admins.
    void ShowAdminPages(WorkspaceViewModel workspace)
    {
        var team = workspace.IsAdmin || workspace.IsPlatformAdmin;
        NavAdminHeading.Visibility = team || workspace.IsAccountManager ? Visibility.Visible : Visibility.Collapsed;
        NavTeam.Visibility = team ? Visibility.Visible : Visibility.Collapsed;
        NavIntake.Visibility = workspace.IsAccountManager ? Visibility.Visible : Visibility.Collapsed;
        NavActivity.Visibility = workspace.IsAdmin ? Visibility.Visible : Visibility.Collapsed;
        NavCompanies.Visibility = workspace.IsPlatformAdmin ? Visibility.Visible : Visibility.Collapsed;
        if ((!team && Nav.SelectedItem == NavTeam) || (!workspace.IsAdmin && Nav.SelectedItem == NavActivity)) Nav.SelectedItem = NavRules;
    }

    // A rail item opens its page; workflow pages hide the valuation summary and Run.
    void Nav_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Nav.SelectedItem is not ListBoxItem item || _pages is null || !_pages.TryGetValue(item, out var page)) return;
        Tabs.SelectedItem = page;
        _viewModel.IsWorkflowPage = _workflowPages.Contains(page);
        if (page == ActivityPage) _ = _workspace.LoadActivityAsync();
        if (page == IntakePage) _ = _workspace.LoadIntakeAsync();
        if (page == RequestsPage) _ = _workspace.LoadRequestsAsync();
        if (!_workflowPages.Contains(page)) _lastValuationPage = (string)page.Header;
    }

    MockGitLabWindow? _mockGitLab;

    // One Mock GitLab window at a time, owned by this one; opening it again brings it forward on the requested issue.
    void OpenMockGitLab(string? project, int? iid)
    {
        if (_workspace.Mock is not { } mock) return;
        if (_mockGitLab is null)
        {
            _mockGitLab = new MockGitLabWindow(new MockGitLabViewModel(mock, project ?? mock.Projects[0].Path)) { Owner = this };
            _mockGitLab.Closed += (_, _) => _mockGitLab = null;
        }
        if (project is not null && iid is int issue) _mockGitLab.Show(project, issue);
        else { _mockGitLab.Show(); _mockGitLab.Activate(); }
    }

    // The last valuation page the person was on, for "Where in Levy" on a new request.
    string _lastValuationPage = "Rules editor";

    // "Parcel map · Parcel 04-2115": the page and selected parcel, filled into a new request.
    string DescribeContext() => _lastValuationPage == (string)MapPage.Header && _viewModel.SelectedCell is not null ? $"{_lastValuationPage} · {_viewModel.SelectedTitle}" : _lastValuationPage;

    // Leaving the Map page closes the full-size map, so the summary strip comes back on other pages.
    // SelectionChanged bubbles up from lists inside the pages too; only the page switch itself counts.
    void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.OriginalSource == Tabs && _viewModel is not null) _viewModel.IsMapExpanded = false;
    }

    // Switches to the Editor tab, then jumps to the line once the tab has been laid out.
    void OpenInEditor(int line)
    {
        Nav.SelectedItem = NavRules;
        Dispatcher.InvokeAsync(() => EditorTab.JumpToLine(line), DispatcherPriority.Loaded);
    }
}
