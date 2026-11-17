using System.Windows;
using System.Windows.Controls;

namespace Workbench.Desktop;

// The Editor tab. Its view-model side is MainViewModel.Editor.cs.
public partial class EditorTabView : UserControl
{
    public EditorTabView() => InitializeComponent();

    // Moves the code view's caret to a line; used by "Open in Editor" on the Map tab.
    public void JumpToLine(int line) => Rules.JumpToLine(line);

    // Clicking a problem jumps the code view to that line.
    void Problem_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ProblemRow { Line: > 0 } problem) Rules.JumpToLine(problem.Line);
    }
}
