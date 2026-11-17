using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;
using ICSharpCode.AvalonEdit.Search;

namespace Workbench.Desktop;

// AvalonEdit wired to the view model's rules text, with problem squiggles, hover help and completion.
public partial class RulesEditorView : UserControl
{
    readonly LineMarks _marks;
    readonly ToolTip _hoverTip = new() { Placement = System.Windows.Controls.Primitives.PlacementMode.Mouse };
    MainViewModel? _viewModel;
    CompletionWindow? _completion;
    bool _syncing;

    public RulesEditorView()
    {
        InitializeComponent();
        _marks = new LineMarks();
        Editor.Options.HighlightCurrentLine = true;
        Editor.Options.ConvertTabsToSpaces = true;
        Editor.Options.IndentationSize = 4;
        Editor.Options.EnableHyperlinks = false;
        Editor.Options.EnableEmailHyperlinks = false;
        Editor.TextArea.SelectionBorder = null;
        Editor.TextArea.SelectionForeground = null;
        Editor.TextArea.SelectionCornerRadius = 2;
        ApplyTheme();
        Editor.TextArea.TextView.BackgroundRenderers.Add(_marks);
        Loaded += (_, _) => ThemeManager.Changed += ApplyTheme;
        Unloaded += (_, _) => ThemeManager.Changed -= ApplyTheme;
        SearchPanel.Install(Editor);

        Editor.TextChanged += (_, _) => OnTextChanged();
        Editor.TextArea.Caret.PositionChanged += (_, _) => CaretStatus.Text = $"Ln {Editor.TextArea.Caret.Line}, Col {Editor.TextArea.Caret.Column}";
        Editor.TextArea.PreviewKeyDown += OnPreviewKeyDown;
        Editor.MouseHover += OnMouseHover;
        Editor.MouseHoverStopped += (_, _) => _hoverTip.IsOpen = false;
        DataContextChanged += (_, _) => Attach(DataContext as MainViewModel);
    }

    // Colours from Assets/Editor/Colors.*.xaml and syntax colours from Assets/Shared/Rules.*.xshd, for the current theme.
    void ApplyTheme()
    {
        Editor.SyntaxHighlighting = Shared.RulesSyntax(ThemeManager.IsDark);
        Editor.TextArea.TextView.CurrentLineBackground = (Brush)FindResource("EditorCurrentLine");
        Editor.TextArea.TextView.CurrentLineBorder = (Pen)FindResource("EditorCurrentLineBorder");
        Editor.TextArea.SelectionBrush = (Brush)FindResource("EditorSelection");
        Editor.TextArea.Caret.CaretBrush = (Brush)FindResource("EditorCaret");
        _marks.ProblemBrush = (Brush)FindResource("EditorProblemTint");
        _marks.SquigglePen = (Pen)FindResource("EditorProblemSquiggle");
        Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
    }

    // Moves the caret to a line, selects it and scrolls it into view.
    public void JumpToLine(int line)
    {
        if (Editor.Document.LineCount == 0) return;
        var target = Editor.Document.GetLineByNumber(Math.Clamp(line, 1, Editor.Document.LineCount));
        Editor.ScrollToLine(target.LineNumber);
        Editor.Select(target.Offset, target.Length);
        Editor.TextArea.Focus();
    }

    void Attach(MainViewModel? viewModel)
    {
        if (_viewModel is not null) _viewModel.PropertyChanged -= OnViewModelChanged;
        _viewModel = viewModel;
        if (viewModel is null) return;
        viewModel.PropertyChanged += OnViewModelChanged;
        PullText();
        RefreshMarks();
    }

    void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(MainViewModel.RulesText)) PullText();
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(MainViewModel.Problems)) RefreshMarks();
    }

    // Copies the view model's text in when another editor changed it, keeping the caret roughly where it was.
    void PullText()
    {
        if (_viewModel is null || Editor.Text == _viewModel.RulesText) return;
        var caret = Editor.CaretOffset;
        _syncing = true;
        Editor.Document.Text = _viewModel.RulesText;
        _syncing = false;
        Editor.CaretOffset = Math.Min(caret, Editor.Document.TextLength);
    }

    void OnTextChanged()
    {
        if (_syncing || _viewModel is null) return;
        _viewModel.RulesText = Editor.Text;
        RefreshMarks();
    }

    // Rebuilds which lines get a problem squiggle.
    void RefreshMarks()
    {
        if (_viewModel is null) return;
        _marks.Problems = _viewModel.Problems.Where(p => p.Line > 0).GroupBy(p => p.Line).ToDictionary(g => g.Key, g => string.Join("\n", g.Select(p => p.Message)));
        Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
    }

    void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space || Keyboard.Modifiers != ModifierKeys.Control) return;
        ShowCompletion();
        e.Handled = true;
    }

    // Offers rule names when the caret is in the name part of a line (before any "=" and not in a comment).
    void ShowCompletion()
    {
        if (_viewModel is null || _completion is not null) return;
        var line = Editor.Document.GetLineByOffset(Editor.CaretOffset);
        var text = Editor.Document.GetText(line);
        var column = Editor.CaretOffset - line.Offset;
        if (text[..column].Contains('=') || text.TrimStart().StartsWith('#')) return;
        var start = column;
        while (start > 0 && (char.IsLetterOrDigit(text[start - 1]) || text[start - 1] is '.' or '-')) start--;
        var hasEquals = text.Contains('=');
        _completion = new CompletionWindow(Editor.TextArea) { StartOffset = line.Offset + start, CloseWhenCaretAtBeginning = false };
        foreach (var (key, description) in _viewModel.RuleCompletions) _completion.CompletionList.CompletionData.Add(new RuleCompletion(key, description, hasEquals));
        _completion.Closed += (_, _) => _completion = null;
        _completion.Show();
        if (column > start) _completion.CompletionList.SelectItem(text[start..column]);
    }

    // Hovering a line shows its problem if it has one, otherwise what its rule does.
    void OnMouseHover(object sender, MouseEventArgs e)
    {
        if (Editor.GetPositionFromPoint(e.GetPosition(Editor)) is not { } position) return;
        var line = Editor.Document.GetLineByNumber(position.Line);
        var text = Editor.Document.GetText(line).Trim();
        var equals = text.IndexOf('=');
        var message = _marks.Problems.TryGetValue(position.Line, out var problem) ? problem
            : equals > 0 && !text.StartsWith('#') ? Shared.DescribeRule(text[..equals].Trim()) : null;
        if (message is null) return;
        _hoverTip.Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 380 };
        _hoverTip.PlacementTarget = Editor;
        _hoverTip.IsOpen = true;
        e.Handled = true;
    }

    // Draws a tint and a squiggle under lines with problems.
    sealed class LineMarks : IBackgroundRenderer
    {
        public Brush ProblemBrush { get; set; } = Brushes.Transparent;
        public Pen SquigglePen { get; set; } = new(Brushes.Transparent, 1);

        public Dictionary<int, string> Problems { get; set; } = new();
        public KnownLayer Layer => KnownLayer.Background;

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            if (!textView.VisualLinesValid) return;
            foreach (var visualLine in textView.VisualLines)
            {
                var number = visualLine.FirstDocumentLine.LineNumber;
                var row = new Rect(0, visualLine.VisualTop - textView.VerticalOffset, textView.ActualWidth, visualLine.Height);
                if (!Problems.ContainsKey(number)) continue;
                drawingContext.DrawRectangle(ProblemBrush, null, row);
                var line = visualLine.FirstDocumentLine;
                var text = textView.Document.GetText(line);
                var lead = text.Length - text.TrimStart().Length;
                var length = text.Trim().Length;
                if (length == 0) continue;
                foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, new TextSegment { StartOffset = line.Offset + lead, Length = length }))
                    drawingContext.DrawGeometry(null, SquigglePen, Squiggle(rect.BottomLeft, rect.Width));
            }
        }

        // A zigzag line under the text, like a compiler error underline.
        static StreamGeometry Squiggle(Point start, double width)
        {
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                context.BeginFigure(new Point(start.X, start.Y - 1), false, false);
                for (double x = 0, step = 2.5; x < width; x += step)
                    context.LineTo(new Point(start.X + x + step, start.Y - 1 + ((int)(x / step) % 2 == 0 ? 2 : 0)), true, false);
            }
            geometry.Freeze();
            return geometry;
        }
    }

    // One rule name in the Ctrl+Space list; picking it fills in the name and, if the line has none yet, " = ".
    sealed class RuleCompletion(string key, string description, bool lineHasEquals) : ICompletionData
    {
        public ImageSource? Image => null;
        public string Text => key;
        public object Content => key;
        public object Description => description;
        public double Priority => 0;

        public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs) =>
            textArea.Document.Replace(completionSegment, lineHasEquals ? key : key + " = ");
    }
}
