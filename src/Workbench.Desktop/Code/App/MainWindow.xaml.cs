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
        var workspace = new WorkspaceViewModel(client, membership);
        foreach (var view in new FrameworkElement[] { JobsView, ProjectsView, BoardView, TasksView, ClientsView, ReportsView, TeamView, CompaniesView }) view.DataContext = workspace;
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
            [NavTeam] = TeamPage, [NavCompanies] = CompaniesPage,
        };
        _workflowPages = [JobsPage, ProjectsPage, BoardPage, TasksPage, ClientsPage, ReportsPage, TeamPage, CompaniesPage];
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

    // The ADMIN section: Team for company admins (and platform admins), Companies for platform admins only.
    void ShowAdminPages(WorkspaceViewModel workspace)
    {
        var team = workspace.IsAdmin || workspace.IsPlatformAdmin;
        NavAdminHeading.Visibility = NavTeam.Visibility = team ? Visibility.Visible : Visibility.Collapsed;
        NavCompanies.Visibility = workspace.IsPlatformAdmin ? Visibility.Visible : Visibility.Collapsed;
        if (!team && Nav.SelectedItem == NavTeam) Nav.SelectedItem = NavRules;
    }

    // A rail item opens its page; workflow pages hide the valuation summary and Run.
    void Nav_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Nav.SelectedItem is not ListBoxItem item || _pages is null || !_pages.TryGetValue(item, out var page)) return;
        Tabs.SelectedItem = page;
        _viewModel.IsWorkflowPage = _workflowPages.Contains(page);
    }

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
