using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using System.Windows.Input;

namespace Workbench.Desktop;

// What a workflow page needs from its table: how to search, filter and count rows, make a new one, check and save a draft.
public sealed record PageRules<T>(
    string Noun,
    string[] FilterValues,
    string AllLabel,
    Func<T, string> SearchText,
    Func<T, string> FilterValue,
    Func<T, Guid?>? Mine,
    Func<T> Create,
    Func<T, string?> Validate,
    Func<IReadOnlyList<T>, IReadOnlyList<SummaryCard>> Counters,
    string? Plural = null,
    string? NewText = null);

// One workflow page's rows and edit form: search, one dropdown filter, "Assigned to me", the selected row, its draft, New / Save / Cancel and the counters.
public sealed class RecordPage<T> : INotifyPropertyChanged where T : Row
{
    readonly PageRules<T> _rules;
    readonly Func<T, bool, Task<T>> _save;
    readonly Guid _me;
    string _search = "";
    string _filter;
    bool _assignedToMe;
    T? _selected;
    T? _draft;
    bool _isNew;
    bool _isSaving;
    string _error = "";

    public RecordPage(WorkspaceViewModel workspace, PageRules<T> rules, Func<T, bool, Task<T>> save, Guid me)
    {
        Workspace = workspace;
        _rules = rules;
        _save = save;
        _me = me;
        _filter = rules.AllLabel;
        FilterOptions = [rules.AllLabel, .. rules.FilterValues];
        View = CollectionViewSource.GetDefaultView(Items);
        View.Filter = item => Matches((T)item);
        NewCommand = new RelayCommand(New);
        SaveCommand = new RelayCommand(() => _ = SaveAsync());
        CancelCommand = new RelayCommand(Cancel);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    // Raised after a save so the workspace can refresh names, counts and pickers on every page.
    public event Action? Saved;

    public WorkspaceViewModel Workspace { get; }
    public string Noun => _rules.Noun;
    public string NewLabel => _rules.NewText ?? "New " + _rules.Noun;
    public string SearchHint => $"Search {_rules.Plural ?? _rules.Noun + "s"}";
    public ObservableCollection<T> Items { get; } = new();
    public ICollectionView View { get; }
    public IReadOnlyList<string> FilterOptions { get; }
    public bool HasMine => _rules.Mine is not null;
    public IReadOnlyList<SummaryCard> Counters { get; private set; } = [];
    public string CountText => $"{View.Cast<object>().Count()} of {Items.Count}";

    public string Search { get => _search; set { _search = value; Raise(); Refilter(); } }
    public string Filter { get => _filter; set { _filter = value ?? _rules.AllLabel; Raise(); Refilter(); } }
    public bool AssignedToMe { get => _assignedToMe; set { _assignedToMe = value; Raise(); Refilter(); } }

    // Picking a row opens it in the form. The table clears its selection while rows are replaced; the form keeps its draft then.
    public T? Selected
    {
        get => _selected;
        set
        {
            if (ReferenceEquals(_selected, value)) return;
            _selected = value;
            Raise();
            if (value is null) return;
            IsNew = false;
            Error = "";
            Draft = (T)value.Copy();
        }
    }

    public T? Draft { get => _draft; private set { _draft = value; Raise(); Raise(nameof(HasDraft)); } }
    public bool HasDraft => _draft is not null;
    public bool IsNew { get => _isNew; private set { _isNew = value; Raise(); Raise(nameof(FormTitle)); } }
    public string FormTitle => _isNew ? NewLabel : "Edit " + _rules.Noun;
    public bool IsSaving { get => _isSaving; private set { _isSaving = value; Raise(); Raise(nameof(SaveLabel)); } }
    public string SaveLabel => _isSaving ? "Saving…" : "Save";
    public string Error { get => _error; private set { _error = value; Raise(); Raise(nameof(HasError)); } }
    public bool HasError => _error.Length > 0;

    public ICommand NewCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }

    public void Load(IEnumerable<T> rows)
    {
        Items.Clear();
        foreach (var row in rows) Items.Add(row);
        _selected = null;
        Draft = null;
        IsNew = false;
        Raise(nameof(Selected));
        Refresh();
    }

    // Re-reads every row after names or counts changed elsewhere, keeping the selection.
    public void Refresh()
    {
        var selected = _selected;
        View.Refresh();
        _selected = selected;
        Raise(nameof(Selected));
        Counters = _rules.Counters(Items);
        Raise(nameof(Counters));
        Raise(nameof(CountText));
    }

    // Puts a stored row in place of the one with the same id (or adds it), as after a save or a board move.
    public void Replace(T stored)
    {
        var index = Items.Select((row, i) => (row, i)).FirstOrDefault(x => x.row.Id == stored.Id, (null!, -1)).Item2;
        if (index >= 0) Items[index] = stored;
        else Items.Insert(0, stored);
    }

    // Opens the form on a row picked somewhere else (a board card).
    public void Open(T row)
    {
        _selected = null;
        Selected = row;
    }

    void New()
    {
        _selected = null;
        Raise(nameof(Selected));
        Error = "";
        IsNew = true;
        Draft = _rules.Create();
    }

    // Drops the draft's changes and closes the form.
    void Cancel()
    {
        Error = "";
        IsNew = false;
        Draft = null;
        _selected = null;
        Raise(nameof(Selected));
    }

    async Task SaveAsync()
    {
        if (_draft is null || _isSaving) return;
        if (_rules.Validate(_draft) is { } problem) { Error = problem; return; }
        IsSaving = true;
        Error = "";
        try
        {
            var stored = await _save(_draft, _isNew);
            Replace(stored);
            Saved?.Invoke();
            _selected = stored;
            Raise(nameof(Selected));
            IsNew = false;
            Draft = (T)stored.Copy();
        }
        catch (SupabaseException e) { Error = e.Message; }
        finally { IsSaving = false; }
    }

    bool Matches(T row)
    {
        if (_filter != _rules.AllLabel && _rules.FilterValue(row) != _filter) return false;
        if (_assignedToMe && _rules.Mine?.Invoke(row) != _me) return false;
        return _search.Length == 0 || _rules.SearchText(row).Contains(_search.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    void Refilter()
    {
        View.Refresh();
        Raise(nameof(CountText));
    }

    void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
