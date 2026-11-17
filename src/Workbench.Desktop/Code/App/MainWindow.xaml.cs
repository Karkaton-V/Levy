using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace Workbench.Desktop;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        FitToScreen();
        // The starting rules and the study area are assets, not strings in code.
        var viewModel = new MainViewModel(Shared.ReadAsset("Editor", "DefaultRules.rules"), StudyArea.Default);
        DataContext = viewModel;
        viewModel.OpenInEditorRequested += OpenInEditor;
        Loaded += async (_, _) => await viewModel.LoadBuildingsAsync();
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

    // Switches to the Editor tab, then jumps to the line once the tab has been laid out.
    void OpenInEditor(int line)
    {
        Tabs.SelectedIndex = 0;
        Dispatcher.InvokeAsync(() => EditorTab.JumpToLine(line), DispatcherPriority.Loaded);
    }
}
