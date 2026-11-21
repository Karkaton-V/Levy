using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Workbench.Desktop;

// Card clicks open the task form; dragging a card onto another column saves its new status.
public partial class BoardPageView : UserControl
{
    WorkTask? _pressed;
    Point _pressedAt;

    public BoardPageView() => InitializeComponent();

    WorkspaceViewModel? Workspace => DataContext as WorkspaceViewModel;

    void Card_Down(object sender, MouseButtonEventArgs e)
    {
        _pressed = (sender as FrameworkElement)?.DataContext as WorkTask;
        _pressedAt = e.GetPosition(this);
    }

    // Starts the drag once the mouse has moved past the system drag distance with the button held.
    void Card_Move(object sender, MouseEventArgs e)
    {
        if (_pressed is not { } task || e.LeftButton != MouseButtonState.Pressed) return;
        var moved = e.GetPosition(this) - _pressedAt;
        if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _pressed = null;
        DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(typeof(WorkTask), task), DragDropEffects.Move);
    }

    // A press and release without a drag is a click: open the task in the form.
    void Card_Up(object sender, MouseButtonEventArgs e)
    {
        if (_pressed is { } task) Workspace?.Tasks.Open(task);
        _pressed = null;
    }

    void Column_DragOver(object sender, DragEventArgs e)
    {
        var isCard = e.Data.GetDataPresent(typeof(WorkTask));
        e.Effects = isCard ? DragDropEffects.Move : DragDropEffects.None;
        if (isCard) ((Border)sender).SetResourceReference(Border.BorderBrushProperty, "Accent");
        e.Handled = true;
    }

    void Column_DragLeave(object sender, DragEventArgs e) => ((Border)sender).ClearValue(Border.BorderBrushProperty);

    async void Column_Drop(object sender, DragEventArgs e)
    {
        var column = (Border)sender;
        column.ClearValue(Border.BorderBrushProperty);
        if (e.Data.GetData(typeof(WorkTask)) is WorkTask task && column.DataContext is BoardColumn target && Workspace is { } workspace)
            await workspace.MoveTaskAsync(task, target.Status);
    }
}
