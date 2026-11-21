using System.Collections.ObjectModel;

namespace Workbench.Desktop;

// One scrum board column: a task status and its cards in board order.
public sealed record BoardColumn(string Status, ObservableCollection<WorkTask> Cards)
{
    public string Count => Cards.Count.ToString();
    public string Points => $"{Cards.Sum(c => c.Points)} pts";
}

// Scrum board page: the tasks in five status columns; dragging a card to another column saves its new status.
public sealed partial class WorkspaceViewModel
{
    Guid _boardProject;
    bool _boardMine;
    string _boardError = "";

    public IReadOnlyList<BoardColumn> Board { get; private set; } = [];

    // Board filters: one project (Guid.Empty for all) and "Assigned to me".
    public Guid BoardProject { get => _boardProject; set { _boardProject = value; Raise(); RebuildBoard(); } }
    public bool BoardMine { get => _boardMine; set { _boardMine = value; Raise(); RebuildBoard(); } }
    public IReadOnlyList<Option> BoardProjects { get; private set; } = [new(Guid.Empty, "All projects")];
    public string BoardError { get => _boardError; private set { _boardError = value; Raise(); Raise(nameof(HasBoardError)); } }
    public bool HasBoardError => _boardError.Length > 0;

    void RebuildBoard()
    {
        var shown = Tasks.Items.Where(t => (_boardProject == Guid.Empty || t.ProjectId == _boardProject) && (!_boardMine || t.AssigneeId == _me)).ToList();
        Board = Choices.TaskStatuses.Select(status => new BoardColumn(status,
            new ObservableCollection<WorkTask>(shown.Where(t => t.Status == status).OrderBy(t => t.SortOrder).ThenBy(t => t.DueDate ?? DateOnly.MaxValue)))).ToList();
        Raise(nameof(Board));
        var projects = (IReadOnlyList<Option>)[new(Guid.Empty, "All projects"), .. ProjectOptions.Skip(1)];
        if (!projects.SequenceEqual(BoardProjects)) { BoardProjects = projects; Raise(nameof(BoardProjects)); }
    }

    // Moves a card to the bottom of another column straight away, then saves; puts it back if the save fails.
    public async Task MoveTaskAsync(WorkTask task, string status)
    {
        if (task.Status == status) return;
        var (oldStatus, oldOrder) = (task.Status, task.SortOrder);
        task.SortOrder = NextSortOrder(status);
        task.Status = status;
        BoardError = "";
        RebuildBoard();
        try
        {
            var stored = await _client.UpdateAsync<WorkTask>("tasks", task.Id, new { task.Status, task.SortOrder });
            Tasks.Replace(stored);
            if (Tasks.Draft?.Id == stored.Id && !Tasks.IsNew) Tasks.Open(stored);
        }
        catch (SupabaseException e)
        {
            (task.Status, task.SortOrder) = (oldStatus, oldOrder);
            BoardError = $"Couldn't move \"{task.Title}\": {e.Message}";
        }
        Relink();
    }
}
